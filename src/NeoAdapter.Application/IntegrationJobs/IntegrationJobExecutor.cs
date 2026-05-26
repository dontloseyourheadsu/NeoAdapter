using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Domain;
using Npgsql;
using MiniExcelLibs;

namespace NeoAdapter.Application.IntegrationJobs;

public sealed class IntegrationJobExecutor(
    NeoAdapterDbContext dbContext,
    ISqlSecretProtector sqlSecretProtector) : IIntegrationJobExecutor
{
    private class SqlTableConfig
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Fields { get; set; } = new();
    }

    private class SqlConfig
    {
        public List<SqlTableConfig> Tables { get; set; } = new();
    }

    public async Task ExecuteAsync(Guid integrationJobId)
    {
        var job = await dbContext.IntegrationJobs
            .Include(j => j.Steps)
                .ThenInclude(s => s.SourceConnector)
            .Include(j => j.Steps)
                .ThenInclude(s => s.DestinationConnector)
            .FirstOrDefaultAsync(j => j.Id == integrationJobId)
            ?? throw new InvalidOperationException("Integration job was not found.");

        var run = await dbContext.IntegrationJobRuns
            .Where(r => r.IntegrationJobId == integrationJobId)
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(r => r.Status == "QUEUED")
            ?? new IntegrationJobRun
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = integrationJobId,
                Status = "QUEUED",
                Message = "Queued for execution.",
                StartedAtUtc = DateTimeOffset.UtcNow,
                RecordsProcessed = 0,
                StartedBy = "Scheduler"
            };

        var isTracked = await dbContext.IntegrationJobRuns.AnyAsync(r => r.Id == run.Id);
        if (!isTracked)
        {
            dbContext.IntegrationJobRuns.Add(run);
        }

        run.Status = "RUNNING";
        run.Message = "Job execution started.";
        run.StartedAtUtc = DateTimeOffset.UtcNow;
        run.FinishedAtUtc = null;
        run.RecordsProcessed = 0;
        await dbContext.SaveChangesAsync();

        try
        {
            await LogInfoAsync(job.Id, run.Id, $"Job execution started. Triggered by: '{run.StartedBy}'", $"Job Name: {job.Name}");
            int totalProcessed = 0;
            var steps = job.Steps.OrderBy(s => s.OrderIndex).ToList();
            
            foreach (var step in steps)
            {
                var source = step.SourceConnector ?? throw new InvalidOperationException($"Source connector is missing for step {step.OrderIndex}.");
                var destination = step.DestinationConnector ?? throw new InvalidOperationException($"Destination connector is missing for step {step.OrderIndex}.");

                await LogInfoAsync(job.Id, run.Id, $"Starting step {step.OrderIndex + 1}: {source.Name} ({source.Type}) -> {destination.Name} ({destination.Type})");

                int processed;
                if (IsSqlConnector(source.Type) && destination.Type == ConnectorType.Csv)
                {
                    processed = await ExecuteSqlToCsvAsync(source, destination, job.Id, run.Id);
                }
                else if (source.Type == ConnectorType.Csv && IsSqlConnector(destination.Type))
                {
                    processed = await ExecuteCsvToSqlAsync(source, destination, job.Id, run.Id);
                }
                else if (IsSqlConnector(source.Type) && destination.Type == ConnectorType.Excel)
                {
                    processed = await ExecuteSqlToExcelAsync(source, destination, job.Id, run.Id);
                }
                else if (source.Type == ConnectorType.Excel && IsSqlConnector(destination.Type))
                {
                    processed = await ExecuteExcelToSqlAsync(source, destination, job.Id, run.Id);
                }
                else if (IsSqlConnector(source.Type) && IsSqlConnector(destination.Type))
                {
                    processed = await ExecuteSqlToSqlAsync(source, destination, job.Id, run.Id);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported connector direction in step {step.OrderIndex}.");
                }
                
                await LogInfoAsync(job.Id, run.Id, $"Step {step.OrderIndex + 1} completed successfully. {processed} records processed.");
                totalProcessed += processed;
            }

            run.Status = "SUCCEEDED";
            run.Message = $"Successfully processed {totalProcessed} record(s) across {steps.Count} step(s).";
            run.RecordsProcessed = totalProcessed;
            run.FinishedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
            await LogInfoAsync(job.Id, run.Id, $"Job execution finished successfully.", run.Message);
        }
        catch (Exception ex)
        {
            run.Status = "FAILED";
            run.Message = ex.Message;
            run.FinishedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
            await LogErrorAsync(job.Id, run.Id, $"Job execution failed: {ex.Message}", ex.ToString());
            throw;
        }
    }

    private static bool IsSqlConnector(ConnectorType type) =>
        type == ConnectorType.SqlServer || type == ConnectorType.Postgres;

    private async Task<int> ExecuteSqlToSqlAsync(Connector source, Connector destination, Guid jobId, Guid runId)
    {
        var sourceConfig = GetSqlConfig(source);
        
        await LogInfoAsync(jobId, runId, $"Connecting to source SQL database '{source.SqlDatabase}' on host '{source.SqlHost}'...");
        await using var sourceConn = await CreateAndOpenSqlConnectionAsync(source);
        await LogInfoAsync(jobId, runId, "Connected to source database successfully.");

        await LogInfoAsync(jobId, runId, $"Connecting to destination SQL database '{destination.SqlDatabase}' on host '{destination.SqlHost}'...");
        await using var destConn = await CreateAndOpenSqlConnectionAsync(destination);
        await LogInfoAsync(jobId, runId, "Connected to destination database successfully.");

        int totalProcessed = 0;
        foreach (var table in sourceConfig.Tables)
        {
            await LogInfoAsync(jobId, runId, $"Transferring table '{table.Name}'...");
            int processed = await TransferTableAsync(sourceConn, destConn, source.Type, destination.Type, table, jobId, runId);
            await LogInfoAsync(jobId, runId, $"Transferred {processed} record(s) from target table '{table.Name}'.");
            totalProcessed += processed;
        }

        return totalProcessed;
    }

    private async Task<int> TransferTableAsync(
        DbConnection sourceConn, 
        DbConnection destConn, 
        ConnectorType sourceType, 
        ConnectorType destType, 
        SqlTableConfig tableConfig,
        Guid jobId,
        Guid runId)
    {
        await EnsureTargetTableExistsAsync(sourceConn, destConn, sourceType, destType, tableConfig, jobId, runId);

        var dbColumns = await GetTableColumnsAsync(destConn, destType, tableConfig.Name);

        var fieldsToTransfer = new List<(string SourceField, string DestColumn)>();
        foreach (var field in tableConfig.Fields)
        {
            if (dbColumns.TryGetValue(field, out var dbColInfo))
            {
                fieldsToTransfer.Add((field, dbColInfo.ExactName));
            }
            else
            {
                fieldsToTransfer.Add((field, field));
            }
        }

        var sourceFieldsSql = string.Join(", ", fieldsToTransfer.Select(f => QuoteIdentifier(sourceType, f.SourceField)));
        var sourceTable = QuoteIdentifier(sourceType, tableConfig.Name);
        
        await using var selectCmd = sourceConn.CreateCommand();
        selectCmd.CommandText = $"SELECT {sourceFieldsSql} FROM {sourceTable}";
        
        await using var reader = await selectCmd.ExecuteReaderAsync();
        
        var destFieldsSql = string.Join(", ", fieldsToTransfer.Select(f => QuoteIdentifier(destType, f.DestColumn)));
        var placeholders = string.Join(", ", fieldsToTransfer.Select((_, i) => $"@p{i}"));
        var destTable = QuoteIdentifier(destType, tableConfig.Name);
        
        var insertSql = $"INSERT INTO {destTable} ({destFieldsSql}) VALUES ({placeholders})";
        
        int count = 0;
        while (await reader.ReadAsync())
        {
            await using var insertCmd = destConn.CreateCommand();
            insertCmd.CommandText = insertSql;
            for (int i = 0; i < fieldsToTransfer.Count; i++)
            {
                var param = insertCmd.CreateParameter();
                param.ParameterName = $"@p{i}";
                param.Value = reader[i] ?? DBNull.Value;
                insertCmd.Parameters.Add(param);
            }
            await insertCmd.ExecuteNonQueryAsync();
            count++;
        }
        
        return count;
    }

    private async Task EnsureTargetTableExistsAsync(
        DbConnection sourceConn, 
        DbConnection destConn, 
        ConnectorType sourceType, 
        ConnectorType destType, 
        SqlTableConfig tableConfig,
        Guid jobId,
        Guid runId)
    {
        bool exists = await CheckTableExistsAsync(destConn, destType, tableConfig.Name);
        if (exists)
        {
            await LogInfoAsync(jobId, runId, $"Target table '{tableConfig.Name}' verified on destination.");
            return;
        }

        throw new InvalidOperationException($"Target table '{tableConfig.Name}' does not exist on destination.");
    }

    private async Task<bool> CheckTableExistsAsync(DbConnection conn, ConnectorType type, string tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = type == ConnectorType.SqlServer
            ? "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name"
            : "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @name";
        
        var param = cmd.CreateParameter();
        param.ParameterName = "@name";
        param.Value = tableName;
        cmd.Parameters.Add(param);
        
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private string QuoteIdentifier(ConnectorType type, string identifier)
    {
        return type == ConnectorType.SqlServer ? $"[{identifier}]" : $"\"{identifier}\"";
    }

    private async Task<int> ExecuteSqlToCsvAsync(Connector sqlConnector, Connector csvConnector, Guid jobId, Guid runId)
    {
        var csvPath = csvConnector.CsvPath;
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new InvalidOperationException("CSV destination path is required.");
        }

        var directory = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sqlConfig = GetSqlConfig(sqlConnector);
        await LogInfoAsync(jobId, runId, $"Connecting to SQL source database '{sqlConnector.SqlDatabase}' on host '{sqlConnector.SqlHost}'...");
        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        await LogInfoAsync(jobId, runId, "Connected to source database successfully.");
        
        int totalProcessed = 0;
        foreach (var tableConfig in sqlConfig.Tables)
        {
            var fields = string.Join(", ", tableConfig.Fields.Select(f => QuoteIdentifier(sqlConnector.Type, f)));
            var sourceTable = QuoteIdentifier(sqlConnector.Type, tableConfig.Name);

            await LogInfoAsync(jobId, runId, $"Reading from SQL table '{tableConfig.Name}'...");
            await using var command = sqlConnection.CreateCommand();
            command.CommandText = $"SELECT {fields} FROM {sourceTable}";

            await using var reader = await command.ExecuteReaderAsync();
            
            var tableCsvPath = sqlConfig.Tables.Count > 1 
                ? Path.Combine(directory!, $"{Path.GetFileNameWithoutExtension(csvPath)}_{tableConfig.Name}{Path.GetExtension(csvPath)}")
                : csvPath;

            await LogInfoAsync(jobId, runId, $"Writing records to CSV file: '{tableCsvPath}'...");
            await using var writer = new StreamWriter(tableCsvPath, false, Encoding.UTF8);
            await using var csvWriter = new CsvWriter(writer, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                Delimiter = csvConnector.CsvDelimiter
            });

            for (var index = 0; index < reader.FieldCount; index++)
            {
                csvWriter.WriteField(reader.GetName(index));
            }

            await csvWriter.NextRecordAsync();

            int tableProcessed = 0;
            while (await reader.ReadAsync())
            {
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    csvWriter.WriteField(reader[index]);
                }

                await csvWriter.NextRecordAsync();
                tableProcessed++;
                totalProcessed++;
            }
            await LogInfoAsync(jobId, runId, $"Successfully wrote {tableProcessed} record(s) to CSV.");
        }

        return totalProcessed;
    }

    private async Task<int> ExecuteCsvToSqlAsync(Connector csvConnector, Connector sqlConnector, Guid jobId, Guid runId)
    {
        var csvPath = csvConnector.CsvPath;
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            throw new InvalidOperationException("CSV source path does not exist.");
        }

        var sqlConfig = GetSqlConfig(sqlConnector);
        if (sqlConfig.Tables.Count == 0)
        {
             throw new InvalidOperationException("SQL target requires at least one table configuration.");
        }
        
        var tableConfig = sqlConfig.Tables[0];
        var table = tableConfig.Name;

        await LogInfoAsync(jobId, runId, $"Connecting to SQL destination database '{sqlConnector.SqlDatabase}' on host '{sqlConnector.SqlHost}'...");
        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        await LogInfoAsync(jobId, runId, "Connected to destination database successfully.");

        await EnsureTargetTableExistsAsync(sqlConnection, sqlConnection, sqlConnector.Type, sqlConnector.Type, tableConfig, jobId, runId);

        var dbColumns = await GetTableColumnsAsync(sqlConnection, sqlConnector.Type, table);

        var delimiter = string.IsNullOrEmpty(csvConnector.CsvDelimiter) ? "," : csvConnector.CsvDelimiter;

        await LogInfoAsync(jobId, runId, $"Opening CSV source file: '{csvPath}'...");
        using var streamReader = new StreamReader(csvPath);
        using var csvReader = new CsvReader(streamReader, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            BadDataFound = null,
            MissingFieldFound = null
        });

        if (!await csvReader.ReadAsync() || !csvReader.ReadHeader())
        {
            await LogWarningAsync(jobId, runId, "CSV file is empty or missing headers.");
            return 0;
        }

        var headers = csvReader.HeaderRecord ?? [];
        if (headers.Length == 0)
        {
            await LogWarningAsync(jobId, runId, "CSV file is empty or missing headers.");
            return 0;
        }

        var fieldsToTransfer = new List<(string CsvHeader, string DbColumn, string DbDataType)>();
        foreach (var configField in tableConfig.Fields)
        {
            var csvHeader = headers.FirstOrDefault(h => string.Equals(h, configField, StringComparison.OrdinalIgnoreCase));
            var dbColFound = dbColumns.TryGetValue(configField, out var dbColInfo);

            if (csvHeader != null && dbColFound)
            {
                fieldsToTransfer.Add((csvHeader, dbColInfo.ExactName, dbColInfo.DataType));
            }
        }

        if (fieldsToTransfer.Count == 0)
        {
            throw new InvalidOperationException($"No matching fields found between CSV headers, configuration, and SQL table '{table}'.");
        }

        string quotedColumns = string.Join(",", fieldsToTransfer.Select(f => QuoteIdentifier(sqlConnector.Type, f.DbColumn)));
        string parametersSql = string.Join(",", fieldsToTransfer.Select((_, index) => $"@p{index}"));
        
        var commandText = $"INSERT INTO {QuoteIdentifier(sqlConnector.Type, table)} ({quotedColumns}) VALUES ({parametersSql})";

        await LogInfoAsync(jobId, runId, $"Inserting CSV records into SQL table '{table}'...");
        var inserted = 0;
        while (await csvReader.ReadAsync())
        {
            await using var insertCommand = sqlConnection.CreateCommand();
            insertCommand.CommandText = commandText;
            for (var index = 0; index < fieldsToTransfer.Count; index++)
            {
                var fieldInfo = fieldsToTransfer[index];
                var rawValue = csvReader.GetField(fieldInfo.CsvHeader);
                var param = insertCommand.CreateParameter();
                param.ParameterName = $"@p{index}";
                param.Value = ConvertValue(rawValue, fieldInfo.DbDataType);
                insertCommand.Parameters.Add(param);
            }

            await insertCommand.ExecuteNonQueryAsync();
            inserted++;
        }

        await LogInfoAsync(jobId, runId, $"Successfully inserted {inserted} record(s) into SQL table '{table}'.");
        return inserted;
    }

    private async Task<DbConnection> CreateAndOpenSqlConnectionAsync(Connector connector)
    {
        DbConnection connection = connector.Type switch
        {
            ConnectorType.SqlServer => new SqlConnection(BuildSqlServerConnectionString(connector)),
            ConnectorType.Postgres => new NpgsqlConnection(BuildPostgresConnectionString(connector)),
            _ => throw new InvalidOperationException("Expected SQL connector.")
        };

        await connection.OpenAsync();
        return connection;
    }

    private SqlConfig GetSqlConfig(Connector connector)
    {
        if (string.IsNullOrWhiteSpace(connector.SqlConfigJson))
        {
             throw new InvalidOperationException($"SQL configuration is missing for connector '{connector.Name}'.");
        }

        return JsonSerializer.Deserialize<SqlConfig>(connector.SqlConfigJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Invalid SQL configuration for connector '{connector.Name}'.");
    }

    private string BuildSqlServerConnectionString(Connector connector)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{connector.SqlHost},{connector.SqlPort ?? 1433}",
            InitialCatalog = connector.SqlDatabase,
            UserID = connector.SqlUsername,
            Password = sqlSecretProtector.Unprotect(connector.SqlPassword ?? string.Empty),
            Encrypt = true,
            TrustServerCertificate = connector.SqlTrustServerCertificate,
            IntegratedSecurity = false,
            ConnectTimeout = 30
        };

        return builder.ConnectionString;
    }

    private string BuildPostgresConnectionString(Connector connector)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = connector.SqlHost,
            Port = connector.SqlPort ?? 5432,
            Database = connector.SqlDatabase,
            Username = connector.SqlUsername,
            Password = sqlSecretProtector.Unprotect(connector.SqlPassword ?? string.Empty),
            TrustServerCertificate = connector.SqlTrustServerCertificate,
            Timeout = 30
        };

        return builder.ConnectionString;
    }

    private async Task<int> ExecuteSqlToExcelAsync(Connector sqlConnector, Connector excelConnector, Guid jobId, Guid runId)
    {
        var excelPath = excelConnector.ExcelPath;
        if (string.IsNullOrWhiteSpace(excelPath))
        {
            throw new InvalidOperationException("Excel destination path is required.");
        }

        var directory = Path.GetDirectoryName(excelPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sqlConfig = GetSqlConfig(sqlConnector);
        await LogInfoAsync(jobId, runId, $"Connecting to SQL source database '{sqlConnector.SqlDatabase}' on host '{sqlConnector.SqlHost}'...");
        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        await LogInfoAsync(jobId, runId, "Connected to source database successfully.");
        
        int totalProcessed = 0;
        foreach (var tableConfig in sqlConfig.Tables)
        {
            var fields = string.Join(", ", tableConfig.Fields.Select(f => QuoteIdentifier(sqlConnector.Type, f)));
            var sourceTable = QuoteIdentifier(sqlConnector.Type, tableConfig.Name);

            await LogInfoAsync(jobId, runId, $"Reading from SQL table '{tableConfig.Name}'...");
            await using var command = sqlConnection.CreateCommand();
            command.CommandText = $"SELECT {fields} FROM {sourceTable}";

            var sheetName = excelConnector.ExcelSheetName ?? tableConfig.Name;
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                sheetName = "Sheet1";
            }

            var tableExcelPath = sqlConfig.Tables.Count > 1 
                ? Path.Combine(directory!, $"{Path.GetFileNameWithoutExtension(excelPath)}_{tableConfig.Name}{Path.GetExtension(excelPath)}")
                : excelPath;

            if (File.Exists(tableExcelPath))
            {
                File.Delete(tableExcelPath);
            }

            {
                await using var reader = await command.ExecuteReaderAsync();
                await LogInfoAsync(jobId, runId, $"Saving Excel sheet '{sheetName}' to: '{tableExcelPath}'...");
                await MiniExcel.SaveAsAsync(tableExcelPath, reader, sheetName: sheetName);
                await reader.CloseAsync();
            }

            await using var countCmd = sqlConnection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {sourceTable}";
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            totalProcessed += count;
            await LogInfoAsync(jobId, runId, $"Saved {count} record(s) to Excel sheet '{sheetName}'.");
        }

        return totalProcessed;
    }

    private async Task<int> ExecuteExcelToSqlAsync(Connector excelConnector, Connector sqlConnector, Guid jobId, Guid runId)
    {
        var excelPath = excelConnector.ExcelPath;
        if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
        {
            throw new InvalidOperationException("Excel source path does not exist.");
        }

        var sqlConfig = GetSqlConfig(sqlConnector);
        if (sqlConfig.Tables.Count == 0)
        {
             throw new InvalidOperationException("SQL target requires at least one table configuration.");
        }
        
        var tableConfig = sqlConfig.Tables[0];
        var table = tableConfig.Name;

        await LogInfoAsync(jobId, runId, $"Connecting to SQL destination database '{sqlConnector.SqlDatabase}' on host '{sqlConnector.SqlHost}'...");
        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        await LogInfoAsync(jobId, runId, "Connected to destination database successfully.");

        await EnsureTargetTableExistsAsync(sqlConnection, sqlConnection, sqlConnector.Type, sqlConnector.Type, tableConfig, jobId, runId);

        var dbColumns = await GetTableColumnsAsync(sqlConnection, sqlConnector.Type, table);

        var sheetName = excelConnector.ExcelSheetName;
        
        await LogInfoAsync(jobId, runId, $"Opening Excel source file: '{excelPath}' (Sheet: '{sheetName ?? "Default"}')...");
        var rowsEnumerable = await MiniExcel.QueryAsync(excelPath, useHeaderRow: true, sheetName: sheetName);
        var rows = rowsEnumerable.Cast<IDictionary<string, object>>().ToList();

        if (rows.Count == 0)
        {
            await LogWarningAsync(jobId, runId, "Excel file is empty or missing data.");
            return 0;
        }

        var headers = rows[0].Keys.ToList();
        var fieldsToTransfer = new List<(string ExcelHeader, string DbColumn, string DbDataType)>();
        foreach (var configField in tableConfig.Fields)
        {
            var excelHeader = headers.FirstOrDefault(h => string.Equals(h, configField, StringComparison.OrdinalIgnoreCase));
            var dbColFound = dbColumns.TryGetValue(configField, out var dbColInfo);

            if (excelHeader != null && dbColFound)
            {
                fieldsToTransfer.Add((excelHeader, dbColInfo.ExactName, dbColInfo.DataType));
            }
        }

        if (fieldsToTransfer.Count == 0)
        {
            throw new InvalidOperationException($"No matching fields found between Excel sheet, configuration, and SQL table '{table}'.");
        }

        string quotedColumns = string.Join(",", fieldsToTransfer.Select(f => QuoteIdentifier(sqlConnector.Type, f.DbColumn)));
        string parametersSql = string.Join(",", fieldsToTransfer.Select((_, index) => $"@p{index}"));
        
        var commandText = $"INSERT INTO {QuoteIdentifier(sqlConnector.Type, table)} ({quotedColumns}) VALUES ({parametersSql})";

        await LogInfoAsync(jobId, runId, $"Inserting Excel records into SQL table '{table}'...");
        var inserted = 0;
        foreach (var row in rows)
        {
            await using var insertCommand = sqlConnection.CreateCommand();
            insertCommand.CommandText = commandText;
            for (var index = 0; index < fieldsToTransfer.Count; index++)
            {
                var fieldInfo = fieldsToTransfer[index];
                
                var key = row.Keys.FirstOrDefault(k => string.Equals(k, fieldInfo.ExcelHeader, StringComparison.OrdinalIgnoreCase));
                var rawValue = key != null ? row[key] : null;

                var param = insertCommand.CreateParameter();
                param.ParameterName = $"@p{index}";
                param.Value = ConvertValue(rawValue, fieldInfo.DbDataType);
                insertCommand.Parameters.Add(param);
            }

            await insertCommand.ExecuteNonQueryAsync();
            inserted++;
        }

        await LogInfoAsync(jobId, runId, $"Successfully inserted {inserted} record(s) into SQL table '{table}'.");
        return inserted;
    }

    private async Task LogInfoAsync(Guid jobId, Guid runId, string message, string? details = null)
    {
        await LogEntryAsync(jobId, runId, "INFO", message, details);
    }

    private async Task LogErrorAsync(Guid jobId, Guid runId, string message, string? details = null)
    {
        await LogEntryAsync(jobId, runId, "ERROR", message, details);
    }

    private async Task LogWarningAsync(Guid jobId, Guid runId, string message, string? details = null)
    {
        await LogEntryAsync(jobId, runId, "WARN", message, details);
    }

    private async Task LogEntryAsync(Guid jobId, Guid runId, string level, string message, string? details)
    {
        try
        {
            var logEntry = new IntegrationJobLogEntry
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = jobId,
                IntegrationJobRunId = runId,
                TimestampUtc = DateTimeOffset.UtcNow,
                LogLevel = level,
                Message = message,
                Details = details
            };

            dbContext.Set<IntegrationJobLogEntry>().Add(logEntry);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing log to DB: {ex.Message}");
        }
    }
    private async Task<Dictionary<string, (string ExactName, string DataType)>> GetTableColumnsAsync(
        DbConnection conn,
        ConnectorType type,
        string tableName)
    {
        var columns = new Dictionary<string, (string ExactName, string DataType)>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = conn.CreateCommand();
        if (type == ConnectorType.SqlServer)
        {
            cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName";
            var param = cmd.CreateParameter();
            param.ParameterName = "@tableName";
            param.Value = tableName;
            cmd.Parameters.Add(param);
        }
        else
        {
            cmd.CommandText = "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = @tableName";
            var param = cmd.CreateParameter();
            param.ParameterName = "@tableName";
            param.Value = tableName;
            cmd.Parameters.Add(param);
        }

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var colName = reader.GetString(0);
                var dataType = reader.GetString(1);
                columns[colName] = (colName, dataType);
            }
        }

        if (columns.Count == 0 && type == ConnectorType.Postgres)
        {
            await using var cmdFallback = conn.CreateCommand();
            cmdFallback.CommandText = "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = @tableName";
            var paramFallback = cmdFallback.CreateParameter();
            paramFallback.ParameterName = "@tableName";
            paramFallback.Value = tableName.ToLowerInvariant();
            cmdFallback.Parameters.Add(paramFallback);

            await using var readerFallback = await cmdFallback.ExecuteReaderAsync();
            while (await readerFallback.ReadAsync())
            {
                var colName = readerFallback.GetString(0);
                var dataType = readerFallback.GetString(1);
                columns[colName] = (colName, dataType);
            }
        }

        return columns;
    }

    private object ConvertValue(object? rawValue, string dbType)
    {
        if (rawValue == null || rawValue is DBNull)
        {
            return DBNull.Value;
        }

        var strVal = rawValue.ToString();
        if (string.IsNullOrWhiteSpace(strVal))
        {
            return DBNull.Value;
        }

        var normalizedType = dbType.ToLowerInvariant();

        if (normalizedType.Contains("int") || normalizedType == "bigint" || normalizedType == "smallint" || normalizedType == "tinyint")
        {
            if (long.TryParse(strVal, out long longVal))
            {
                if (normalizedType == "int" || normalizedType == "integer")
                {
                    return (int)longVal;
                }
                if (normalizedType == "smallint")
                {
                    return (short)longVal;
                }
                if (normalizedType == "tinyint")
                {
                    return (byte)longVal;
                }
                return longVal;
            }
        }

        if (normalizedType.Contains("decimal") || normalizedType.Contains("numeric") || normalizedType.Contains("double") || normalizedType == "real" || normalizedType == "float")
        {
            if (decimal.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal decVal))
            {
                if (normalizedType == "real" || normalizedType == "float")
                {
                    return (double)decVal;
                }
                return decVal;
            }
            else if (double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double doubleVal))
            {
                return doubleVal;
            }
        }

        if (normalizedType == "boolean" || normalizedType == "bool" || normalizedType == "bit")
        {
            if (bool.TryParse(strVal, out bool boolVal))
            {
                return boolVal;
            }
            if (strVal == "1" || strVal.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (strVal == "0" || strVal.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (normalizedType == "uuid" || normalizedType == "uniqueidentifier")
        {
            if (Guid.TryParse(strVal, out Guid guidVal))
            {
                return guidVal;
            }
        }

        if (normalizedType.Contains("date") || normalizedType.Contains("time"))
        {
            if (DateTime.TryParse(strVal, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dtVal))
            {
                return dtVal;
            }
            if (DateTime.TryParse(strVal, out DateTime dtValLocal))
            {
                return dtValLocal;
            }
        }

        return rawValue;
    }
}
