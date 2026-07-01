using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Domain;
using Npgsql;
using MiniExcelLibs;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Application.Connectors;
using ConnectorType = NeoAdapter.Domain.ConnectorType;

namespace NeoAdapter.Application.IntegrationJobs;

public sealed class IntegrationJobExecutor(
    NeoAdapterDbContext dbContext,
    ISqlSecretProtector sqlSecretProtector,
    ISharePointApiClient sharePointApiClient,
    IOutlookCalendarApiClient outlookCalendarApiClient) : IIntegrationJobExecutor
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
                else if (source.Type == ConnectorType.Path && destination.Type == ConnectorType.Sftp)
                {
                    processed = await ExecutePathToSftpAsync(source, destination, job.Id, run.Id);
                }
                else if (source.Type == ConnectorType.Sftp && destination.Type == ConnectorType.Path)
                {
                    processed = await ExecuteSftpToPathAsync(source, destination, job.Id, run.Id);
                }
                else if (source.Type == ConnectorType.SharePoint && IsSqlConnector(destination.Type))
                {
                    processed = await ExecuteSharePointToSqlAsync(source, destination, job, run.Id);
                }
                else if (IsSqlConnector(source.Type) && destination.Type == ConnectorType.SharePoint)
                {
                    processed = await ExecuteSqlToSharePointAsync(source, destination, job, run.Id);
                }
                else if (destination.Type == ConnectorType.OutlookCalendar)
                {
                    processed = await ExecuteSourceToOutlookCalendarAsync(source, destination, job, run.Id);
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

    private async Task<int> ExecutePathToSftpAsync(Connector pathConnector, Connector sftpConnector, Guid jobId, Guid runId)
    {
        var localPath = pathConnector.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new InvalidOperationException("Local path is required.");
        }

        var host = sftpConnector.SftpHost;
        var port = sftpConnector.SftpPort ?? 22;
        var username = sftpConnector.SftpUsername;
        var password = sqlSecretProtector.Unprotect(sftpConnector.SftpPassword ?? string.Empty);
        var remotePath = sftpConnector.SftpRemotePath;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(remotePath))
        {
            throw new InvalidOperationException("SFTP connector requires host, username, and remote path.");
        }

        await LogInfoAsync(jobId, runId, $"Connecting to SFTP destination '{host}:{port}'...");
        using var client = new Renci.SshNet.SftpClient(host, port, username, password);
        await Task.Run(() => client.Connect());
        await LogInfoAsync(jobId, runId, "Connected to SFTP server successfully.");

        int processed = 0;
        if (Directory.Exists(localPath))
        {
            var files = Directory.GetFiles(localPath);
            await LogInfoAsync(jobId, runId, $"Scanning local directory '{localPath}'. Found {files.Length} file(s) to upload.");
            
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destPath = remotePath;
                try
                {
                    if (client.GetAttributes(remotePath).IsDirectory)
                    {
                        destPath = remotePath.TrimEnd('/') + "/" + fileName;
                    }
                }
                catch
                {
                    // Target doesn't exist yet or isn't a directory
                }

                await LogInfoAsync(jobId, runId, $"Uploading '{fileName}' to '{destPath}'...");
                using var fs = File.OpenRead(file);
                await Task.Run(() => client.UploadFile(fs, destPath));
                processed++;
            }
        }
        else if (File.Exists(localPath))
        {
            var fileName = Path.GetFileName(localPath);
            var destPath = remotePath;
            try
            {
                if (client.GetAttributes(remotePath).IsDirectory)
                {
                    destPath = remotePath.TrimEnd('/') + "/" + fileName;
                }
            }
            catch
            {
                // Target doesn't exist yet or isn't a directory
            }

            await LogInfoAsync(jobId, runId, $"Uploading single file '{fileName}' to '{destPath}'...");
            using var fs = File.OpenRead(localPath);
            await Task.Run(() => client.UploadFile(fs, destPath));
            processed = 1;
        }
        else
        {
            throw new InvalidOperationException($"Local path '{localPath}' does not exist.");
        }

        await Task.Run(() => client.Disconnect());
        return processed;
    }

    private async Task<int> ExecuteSftpToPathAsync(Connector sftpConnector, Connector pathConnector, Guid jobId, Guid runId)
    {
        var localPath = pathConnector.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new InvalidOperationException("Local path is required.");
        }

        var host = sftpConnector.SftpHost;
        var port = sftpConnector.SftpPort ?? 22;
        var username = sftpConnector.SftpUsername;
        var password = sqlSecretProtector.Unprotect(sftpConnector.SftpPassword ?? string.Empty);
        var remotePath = sftpConnector.SftpRemotePath;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(remotePath))
        {
            throw new InvalidOperationException("SFTP connector requires host, username, and remote path.");
        }

        await LogInfoAsync(jobId, runId, $"Connecting to SFTP source '{host}:{port}'...");
        using var client = new Renci.SshNet.SftpClient(host, port, username, password);
        await Task.Run(() => client.Connect());
        await LogInfoAsync(jobId, runId, "Connected to SFTP server successfully.");

        if (!client.Exists(remotePath))
        {
            throw new InvalidOperationException($"Remote path '{remotePath}' does not exist on SFTP server.");
        }

        var attrs = client.GetAttributes(remotePath);
        int processed = 0;

        if (attrs.IsDirectory)
        {
            if (!Directory.Exists(localPath))
            {
                Directory.CreateDirectory(localPath);
            }

            var files = client.ListDirectory(remotePath);
            await LogInfoAsync(jobId, runId, $"Scanning remote SFTP directory '{remotePath}'...");

            foreach (var file in files)
            {
                if (file.Name == "." || file.Name == "..") continue;
                if (file.IsDirectory) continue;

                var localFilePath = Path.Combine(localPath, file.Name);
                await LogInfoAsync(jobId, runId, $"Downloading '{file.FullName}' to '{localFilePath}'...");
                using (var fs = File.Create(localFilePath))
                {
                    await Task.Run(() => client.DownloadFile(file.FullName, fs));
                }
                processed++;
            }
        }
        else
        {
            var localFilePath = localPath;
            if (Directory.Exists(localPath))
            {
                var fileName = Path.GetFileName(remotePath);
                localFilePath = Path.Combine(localPath, fileName);
            }
            else
            {
                var parentDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }
            }

            await LogInfoAsync(jobId, runId, $"Downloading single file '{remotePath}' to '{localFilePath}'...");
            using (var fs = File.Create(localFilePath))
            {
                await Task.Run(() => client.DownloadFile(remotePath, fs));
            }
            processed = 1;
        }

        await Task.Run(() => client.Disconnect());
        return processed;
    }

    private async Task<int> ExecuteSharePointToSqlAsync(
        Connector source, 
        Connector destination, 
        IntegrationJob job, 
        Guid runId)
    {
        var executingUserId = job.OwnerUserId ?? job.CreatorUserId;
        if (executingUserId == null)
        {
            throw new InvalidOperationException("No owner or creator user is assigned to this job.");
        }
        var ownerUser = await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == executingUserId);
        if (ownerUser == null || string.IsNullOrEmpty(ownerUser.MicrosoftId))
        {
            throw new InvalidOperationException("Execution requires that the job owner's account be authenticated with Microsoft.");
        }

        var siteUrl = source.SharePointSiteUrl;
        var listName = source.SharePointListName;
        if (string.IsNullOrWhiteSpace(siteUrl) || string.IsNullOrWhiteSpace(listName))
        {
            throw new InvalidOperationException("SharePoint connector requires Site URL and List Name.");
        }

        await LogInfoAsync(job.Id, runId, $"Obtaining Microsoft access token for SharePoint site '{siteUrl}'...");
        var token = await sharePointApiClient.GetAccessTokenAsync(siteUrl, CancellationToken.None);
        await LogInfoAsync(job.Id, runId, "Access token obtained successfully.");

        await LogInfoAsync(job.Id, runId, $"Fetching fields metadata for list '{listName}'...");
        var fields = await sharePointApiClient.GetFieldsAsync(siteUrl, listName, token, CancellationToken.None);
        await LogInfoAsync(job.Id, runId, $"Retrieved {fields.Count} field(s) from SharePoint list.");

        var sqlConfig = GetSqlConfig(destination);
        await LogInfoAsync(job.Id, runId, $"Connecting to destination SQL database '{destination.SqlDatabase}'...");
        await using var destConn = await CreateAndOpenSqlConnectionAsync(destination);
        await LogInfoAsync(job.Id, runId, "Connected to destination database successfully.");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json;odata=verbose");

        var uri = new Uri(siteUrl);
        var absolutePath = uri.AbsolutePath.TrimEnd('/');
        var targetUrl = $"{uri.Scheme}://{uri.Host}{absolutePath}/_api/web/lists/GetByTitle('{Uri.EscapeDataString(listName)}')/items?$select=*";

        var expands = new List<string>();
        var extraSelects = new List<string>();
        foreach (var f in fields)
        {
            if (f.TypeAsString == "User" || f.TypeAsString == "UserMulti")
            {
                extraSelects.Add($"{f.InternalName}/EMail");
                extraSelects.Add($"{f.InternalName}/Title");
                expands.Add(f.InternalName);
            }
            else if (f.TypeAsString == "Lookup" || f.TypeAsString == "LookupMulti")
            {
                extraSelects.Add($"{f.InternalName}/Title");
                expands.Add(f.InternalName);
            }
        }
        if (extraSelects.Count > 0)
        {
            targetUrl += "," + string.Join(",", extraSelects);
        }
        if (expands.Count > 0)
        {
            targetUrl += "&$expand=" + string.Join(",", expands);
        }

        await LogInfoAsync(job.Id, runId, $"Requesting items from SharePoint: {targetUrl}");
        var response = await client.GetAsync(targetUrl);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to fetch list items from SharePoint: {response.StatusCode} - {err}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var itemsList = new List<JsonElement>();

        if (doc.RootElement.TryGetProperty("d", out var dProp) && dProp.TryGetProperty("results", out var resultsProp))
        {
            foreach (var item in resultsProp.EnumerateArray())
            {
                itemsList.Add(item);
            }
        }

        await LogInfoAsync(job.Id, runId, $"Fetched {itemsList.Count} items from SharePoint list.");

        int totalProcessed = 0;
        foreach (var table in sqlConfig.Tables)
        {
            await LogInfoAsync(job.Id, runId, $"Writing to destination SQL table '{table.Name}'...");
            await EnsureTargetTableExistsAsync(destConn, destConn, destination.Type, destination.Type, table, job.Id, runId);

            var dbColumns = await GetTableColumnsAsync(destConn, destination.Type, table.Name);

            var columnMappings = new List<(string SqlColumn, Func<JsonElement, object?> MapValue)>();

            foreach (var fieldName in table.Fields)
            {
                var sqlColName = dbColumns.TryGetValue(fieldName, out var dbColInfo) ? dbColInfo.ExactName : fieldName;

                var matchedField = fields.FirstOrDefault(f => 
                    string.Equals(f.InternalName, fieldName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.Title, fieldName, StringComparison.OrdinalIgnoreCase));

                if (matchedField != null)
                {
                    columnMappings.Add((sqlColName, item => ExtractSharePointValue(item, matchedField)));
                }
                else
                {
                    var matchedSuffixField = fields.FirstOrDefault(f => 
                        fieldName.StartsWith(f.InternalName, StringComparison.OrdinalIgnoreCase) ||
                        fieldName.StartsWith(f.Title, StringComparison.OrdinalIgnoreCase));

                    if (matchedSuffixField != null)
                    {
                        var suffix = fieldName.Substring(matchedSuffixField.InternalName.Length);
                        if (string.IsNullOrEmpty(suffix))
                        {
                            suffix = fieldName.Substring(matchedSuffixField.Title.Length);
                        }

                        columnMappings.Add((sqlColName, item => ExtractSharePointSuffixValue(item, matchedSuffixField, suffix)));
                    }
                    else
                    {
                        columnMappings.Add((sqlColName, item => 
                        {
                            if (item.TryGetProperty(fieldName, out var val))
                            {
                                return GetJsonValue(val);
                            }
                            return null;
                        }));
                    }
                }
            }

            var destFieldsSql = string.Join(", ", columnMappings.Select(m => QuoteIdentifier(destination.Type, m.SqlColumn)));
            var placeholders = string.Join(", ", columnMappings.Select((_, i) => $"@p{i}"));
            var destTable = QuoteIdentifier(destination.Type, table.Name);
            var insertSql = $"INSERT INTO {destTable} ({destFieldsSql}) VALUES ({placeholders})";

            int tableCount = 0;
            foreach (var item in itemsList)
            {
                await using var insertCmd = destConn.CreateCommand();
                insertCmd.CommandText = insertSql;

                for (int i = 0; i < columnMappings.Count; i++)
                {
                    var param = insertCmd.CreateParameter();
                    param.ParameterName = $"@p{i}";
                    var val = columnMappings[i].MapValue(item);
                    param.Value = val ?? DBNull.Value;
                    insertCmd.Parameters.Add(param);
                }

                await insertCmd.ExecuteNonQueryAsync();
                tableCount++;
            }

            await LogInfoAsync(job.Id, runId, $"Wrote {tableCount} record(s) to table '{table.Name}'.");
            totalProcessed += tableCount;
        }

        return totalProcessed;
    }

    private object? ExtractSharePointValue(JsonElement item, SharePointFieldDto field)
    {
        if (field.TypeAsString == "User" || field.TypeAsString == "UserMulti")
        {
            if (item.TryGetProperty(field.InternalName, out var userProp) && userProp.ValueKind == JsonValueKind.Object)
            {
                if (userProp.TryGetProperty("EMail", out var emailProp)) return emailProp.GetString();
                if (userProp.TryGetProperty("Title", out var titleProp)) return titleProp.GetString();
            }
            if (item.TryGetProperty($"{field.InternalName}Id", out var idProp))
            {
                return GetJsonValue(idProp);
            }
        }
        else if (field.TypeAsString == "Lookup" || field.TypeAsString == "LookupMulti")
        {
            if (item.TryGetProperty(field.InternalName, out var lookupProp) && lookupProp.ValueKind == JsonValueKind.Object)
            {
                if (lookupProp.TryGetProperty("Title", out var titleProp)) return titleProp.GetString();
            }
            if (item.TryGetProperty($"{field.InternalName}Id", out var idProp))
            {
                return GetJsonValue(idProp);
            }
        }

        if (item.TryGetProperty(field.InternalName, out var val))
        {
            return GetJsonValue(val);
        }
        return null;
    }

    private object? ExtractSharePointSuffixValue(JsonElement item, SharePointFieldDto field, string suffix)
    {
        if (string.Equals(suffix, "Id", StringComparison.OrdinalIgnoreCase))
        {
            if (item.TryGetProperty($"{field.InternalName}Id", out var idProp)) return GetJsonValue(idProp);
            if (item.TryGetProperty(field.InternalName, out var val) && val.TryGetProperty("Id", out var nestedId)) return GetJsonValue(nestedId);
        }
        else if (string.Equals(suffix, "Email", StringComparison.OrdinalIgnoreCase) || string.Equals(suffix, "EMail", StringComparison.OrdinalIgnoreCase))
        {
            if (item.TryGetProperty(field.InternalName, out var val) && val.TryGetProperty("EMail", out var nestedEmail)) return nestedEmail.GetString();
        }
        else if (string.Equals(suffix, "Name", StringComparison.OrdinalIgnoreCase) || string.Equals(suffix, "Title", StringComparison.OrdinalIgnoreCase))
        {
            if (item.TryGetProperty(field.InternalName, out var val) && val.TryGetProperty("Title", out var nestedTitle)) return nestedTitle.GetString();
        }

        return ExtractSharePointValue(item, field);
    }

    private object? GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
        };
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _sharePointUserCache = new();

    private async Task<int?> GetSharePointUserIdAsync(string siteUrl, string email, string token)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        email = email.Trim();
        if (_sharePointUserCache.TryGetValue(email, out var cachedId)) return cachedId;

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json;odata=verbose");

            var uri = new Uri(siteUrl);
            var absolutePath = uri.AbsolutePath.TrimEnd('/');
            var targetUrl = $"{uri.Scheme}://{uri.Host}{absolutePath}/_api/web/siteusers/getByEmail('{Uri.EscapeDataString(email)}')?$select=Id";

            var response = await client.GetAsync(targetUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("d", out var dProp) && dProp.TryGetProperty("Id", out var idProp))
                {
                    var id = idProp.GetInt32();
                    _sharePointUserCache[email] = id;
                    return id;
                }
            }
        }
        catch
        {
            // Fall through
        }
        return null;
    }

    private async Task<string> GetSharePointListItemEntityTypeAsync(string siteUrl, string listName, string token)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json;odata=verbose");

            var uri = new Uri(siteUrl);
            var absolutePath = uri.AbsolutePath.TrimEnd('/');
            var targetUrl = $"{uri.Scheme}://{uri.Host}{absolutePath}/_api/web/lists/GetByTitle('{Uri.EscapeDataString(listName)}')?$select=ListItemEntityTypeFullName";

            var response = await client.GetAsync(targetUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("d", out var dProp) && dProp.TryGetProperty("ListItemEntityTypeFullName", out var typeProp))
                {
                    return typeProp.GetString() ?? $"SP.Data.{listName.Replace(" ", "_x0020_")}ListItem";
                }
            }
        }
        catch
        {
            // Fall through
        }
        return $"SP.Data.{listName.Replace(" ", "_x0020_")}ListItem";
    }

    private async Task<int> ExecuteSqlToSharePointAsync(
        Connector source, 
        Connector destination, 
        IntegrationJob job, 
        Guid runId)
    {
        var executingUserId = job.OwnerUserId ?? job.CreatorUserId;
        if (executingUserId == null)
        {
            throw new InvalidOperationException("No owner or creator user is assigned to this job.");
        }
        var ownerUser = await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == executingUserId);
        if (ownerUser == null || string.IsNullOrEmpty(ownerUser.MicrosoftId))
        {
            throw new InvalidOperationException("Execution requires that the job owner's account be authenticated with Microsoft.");
        }

        var siteUrl = destination.SharePointSiteUrl;
        var listName = destination.SharePointListName;
        if (string.IsNullOrWhiteSpace(siteUrl) || string.IsNullOrWhiteSpace(listName))
        {
            throw new InvalidOperationException("SharePoint connector requires Site URL and List Name.");
        }

        await LogInfoAsync(job.Id, runId, $"Obtaining Microsoft access token for SharePoint site '{siteUrl}'...");
        var token = await sharePointApiClient.GetAccessTokenAsync(siteUrl, CancellationToken.None);
        await LogInfoAsync(job.Id, runId, "Access token obtained successfully.");

        await LogInfoAsync(job.Id, runId, $"Fetching fields metadata for list '{listName}'...");
        var fields = await sharePointApiClient.GetFieldsAsync(siteUrl, listName, token, CancellationToken.None);
        await LogInfoAsync(job.Id, runId, $"Retrieved {fields.Count} field(s) from SharePoint list.");

        await LogInfoAsync(job.Id, runId, $"Fetching list entity type name...");
        var listItemEntityType = await GetSharePointListItemEntityTypeAsync(siteUrl, listName, token);
        await LogInfoAsync(job.Id, runId, $"Entity type name is '{listItemEntityType}'.");

        var sqlConfig = GetSqlConfig(source);
        await LogInfoAsync(job.Id, runId, $"Connecting to source SQL database '{source.SqlDatabase}'...");
        await using var sourceConn = await CreateAndOpenSqlConnectionAsync(source);
        await LogInfoAsync(job.Id, runId, "Connected to source database successfully.");

        int totalProcessed = 0;
        foreach (var table in sqlConfig.Tables)
        {
            await LogInfoAsync(job.Id, runId, $"Reading from SQL table '{table.Name}'...");
            
            var fieldsToRead = string.Join(", ", table.Fields.Select(f => QuoteIdentifier(source.Type, f)));
            var sourceTable = QuoteIdentifier(source.Type, table.Name);

            await using var selectCmd = sourceConn.CreateCommand();
            selectCmd.CommandText = $"SELECT {fieldsToRead} FROM {sourceTable}";

            await using var reader = await selectCmd.ExecuteReaderAsync();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            var uri = new Uri(siteUrl);
            var absolutePath = uri.AbsolutePath.TrimEnd('/');
            var postUrl = $"{uri.Scheme}://{uri.Host}{absolutePath}/_api/web/lists/GetByTitle('{Uri.EscapeDataString(listName)}')/items";

            int tableCount = 0;
            while (await reader.ReadAsync())
            {
                var payload = new Dictionary<string, object>();
                payload["__metadata"] = new Dictionary<string, string> { { "type", listItemEntityType } };

                for (int i = 0; i < table.Fields.Count; i++)
                {
                    var sqlColName = table.Fields[i];
                    var val = reader[i];
                    if (val == null || val == DBNull.Value) continue;

                    var matchedField = fields.FirstOrDefault(f => 
                        string.Equals(f.InternalName, sqlColName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.Title, sqlColName, StringComparison.OrdinalIgnoreCase));

                    if (matchedField != null)
                    {
                        if (matchedField.TypeAsString == "User" || matchedField.TypeAsString == "UserMulti")
                        {
                            if (val is int intUserId)
                            {
                                payload[$"{matchedField.InternalName}Id"] = intUserId;
                            }
                            else if (val is string emailStr)
                            {
                                var spUserId = await GetSharePointUserIdAsync(siteUrl, emailStr, token);
                                if (spUserId.HasValue)
                                {
                                    payload[$"{matchedField.InternalName}Id"] = spUserId.Value;
                                }
                            }
                        }
                        else if (matchedField.TypeAsString == "Lookup" || matchedField.TypeAsString == "LookupMulti")
                        {
                            if (val is int intLookupId)
                            {
                                payload[$"{matchedField.InternalName}Id"] = intLookupId;
                            }
                            else if (int.TryParse(val.ToString(), out var parsedId))
                            {
                                payload[$"{matchedField.InternalName}Id"] = parsedId;
                            }
                        }
                        else if (matchedField.TypeAsString == "Boolean")
                        {
                            payload[matchedField.InternalName] = Convert.ToBoolean(val);
                        }
                        else if (matchedField.TypeAsString == "Number")
                        {
                            payload[matchedField.InternalName] = Convert.ToDouble(val);
                        }
                        else if (matchedField.TypeAsString == "DateTime")
                        {
                            if (val is DateTime dt)
                            {
                                payload[matchedField.InternalName] = dt.ToString("o");
                            }
                            else if (val is DateTimeOffset dto)
                            {
                                payload[matchedField.InternalName] = dto.ToString("o");
                            }
                            else
                            {
                                payload[matchedField.InternalName] = val.ToString()!;
                            }
                        }
                        else
                        {
                            payload[matchedField.InternalName] = val.ToString()!;
                        }
                    }
                    else
                    {
                        payload[sqlColName] = val.ToString()!;
                    }
                }

                using var requestMsg = new HttpRequestMessage(HttpMethod.Post, postUrl);
                requestMsg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                requestMsg.Headers.Accept.ParseAdd("application/json;odata=verbose");
                
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json;odata=verbose");
                requestMsg.Content = content;

                var postResponse = await client.SendAsync(requestMsg);
                if (!postResponse.IsSuccessStatusCode)
                {
                    var postErr = await postResponse.Content.ReadAsStringAsync();
                    throw new InvalidOperationException($"Failed to write item to SharePoint: {postResponse.StatusCode} - {postErr}");
                }

                tableCount++;
            }

            await LogInfoAsync(job.Id, runId, $"Wrote {tableCount} record(s) from SQL table '{table.Name}' to SharePoint list.");
            totalProcessed += tableCount;
        }

        return totalProcessed;
    }

    private async Task<int> ExecuteSourceToOutlookCalendarAsync(
        Connector source, 
        Connector destination, 
        IntegrationJob job, 
        Guid runId)
    {
        var executingUserId = job.OwnerUserId ?? job.CreatorUserId;
        if (executingUserId == null)
        {
            throw new InvalidOperationException("No owner or creator user is assigned to this job.");
        }
        var ownerUser = await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == executingUserId);
        if (ownerUser == null || string.IsNullOrEmpty(ownerUser.MicrosoftId))
        {
            throw new InvalidOperationException("Execution requires that the job owner's account be authenticated with Microsoft.");
        }

        var calendarName = destination.OutlookCalendarName;

        await LogInfoAsync(job.Id, runId, "Obtaining Microsoft access token for Graph API...");
        var token = await outlookCalendarApiClient.GetAccessTokenAsync(CancellationToken.None);
        await LogInfoAsync(job.Id, runId, "Access token obtained successfully.");

        List<Dictionary<string, object>> rows;
        if (source.Type == ConnectorType.Csv)
        {
            await LogInfoAsync(job.Id, runId, $"Reading rows from CSV source '{source.CsvPath}'...");
            rows = await ReadCsvRowsAsync(source);
        }
        else if (IsSqlConnector(source.Type))
        {
            await LogInfoAsync(job.Id, runId, $"Reading rows from SQL source...");
            rows = await ReadSqlRowsAsync(source);
        }
        else if (source.Type == ConnectorType.Excel)
        {
            await LogInfoAsync(job.Id, runId, $"Reading rows from Excel source '{source.ExcelPath}'...");
            rows = await ReadExcelRowsAsync(source);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported source type '{source.Type}' for Outlook destination.");
        }

        await LogInfoAsync(job.Id, runId, $"Read {rows.Count} row(s) from source. Commencing Outlook Event creation...");

        int count = 0;
        foreach (var row in rows)
        {
            var eventPayload = MapRowToEventPayload(row, destination.OutlookConfigJson);
            await outlookCalendarApiClient.CreateEventAsync(ownerUser.MicrosoftId, calendarName, token, eventPayload, CancellationToken.None);
            count++;
        }

        await LogInfoAsync(job.Id, runId, $"Successfully created {count} event(s) in Outlook calendar '{calendarName}'.");
        return count;
    }

    private async Task<List<Dictionary<string, object>>> ReadCsvRowsAsync(Connector csvConnector)
    {
        var csvPath = csvConnector.CsvPath;
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            throw new InvalidOperationException("CSV source path does not exist.");
        }

        var delimiter = string.IsNullOrEmpty(csvConnector.CsvDelimiter) ? "," : csvConnector.CsvDelimiter;
        using var streamReader = new StreamReader(csvPath);
        using var csvReader = new CsvReader(streamReader, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            BadDataFound = null,
            MissingFieldFound = null
        });

        if (!await csvReader.ReadAsync() || !csvReader.ReadHeader())
        {
            return [];
        }

        var headers = csvReader.HeaderRecord ?? [];
        var rows = new List<Dictionary<string, object>>();

        while (await csvReader.ReadAsync())
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
            {
                row[h] = csvReader.GetField(h) ?? string.Empty;
            }
            rows.Add(row);
        }

        return rows;
    }

    private object MapRowToEventPayload(Dictionary<string, object> row, string? configJson)
    {
        string subjectKey = "Subject";
        string bodyKey = "Body";
        string startKey = "Start";
        string endKey = "End";
        string locationKey = "Location";
        string isAllDayKey = "IsAllDay";
        
        string recTypeKey = "RecurrenceType";
        string recIntervalKey = "RecurrenceInterval";
        string recDaysOfWeekKey = "RecurrenceDaysOfWeek";
        string recRangeTypeKey = "RecurrenceRangeType";
        string recEndDateKey = "RecurrenceEndDate";
        string recOccurrencesKey = "RecurrenceOccurrences";

        if (!string.IsNullOrEmpty(configJson))
        {
            try
            {
                using var configDoc = JsonDocument.Parse(configJson);
                var root = configDoc.RootElement;
                if (root.TryGetProperty("subjectColumn", out var prop)) subjectKey = prop.GetString() ?? subjectKey;
                if (root.TryGetProperty("bodyColumn", out var propBody)) bodyKey = propBody.GetString() ?? bodyKey;
                if (root.TryGetProperty("startColumn", out var propStart)) startKey = propStart.GetString() ?? startKey;
                if (root.TryGetProperty("endColumn", out var propEnd)) endKey = propEnd.GetString() ?? endKey;
                if (root.TryGetProperty("locationColumn", out var propLoc)) locationKey = propLoc.GetString() ?? locationKey;
                if (root.TryGetProperty("isAllDayColumn", out var propAllDay)) isAllDayKey = propAllDay.GetString() ?? isAllDayKey;
                if (root.TryGetProperty("recurrenceTypeColumn", out var propRecType)) recTypeKey = propRecType.GetString() ?? recTypeKey;
                if (root.TryGetProperty("recurrenceIntervalColumn", out var propRecInt)) recIntervalKey = propRecInt.GetString() ?? recIntervalKey;
                if (root.TryGetProperty("recurrenceDaysOfWeekColumn", out var propRecDays)) recDaysOfWeekKey = propRecDays.GetString() ?? recDaysOfWeekKey;
                if (root.TryGetProperty("recurrenceRangeTypeColumn", out var propRecRange)) recRangeTypeKey = propRecRange.GetString() ?? recRangeTypeKey;
                if (root.TryGetProperty("recurrenceEndDateColumn", out var propRecEnd)) recEndDateKey = propRecEnd.GetString() ?? recEndDateKey;
                if (root.TryGetProperty("recurrenceOccurrencesColumn", out var propRecOcc)) recOccurrencesKey = propRecOcc.GetString() ?? recOccurrencesKey;
            }
            catch { }
        }

        object? GetValue(string key)
        {
            if (row.TryGetValue(key, out var val)) return val;
            foreach (var kvp in row)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)) return kvp.Value;
            }
            return null;
        }

        var subject = GetValue(subjectKey)?.ToString() ?? "Untitled Event";
        var body = GetValue(bodyKey)?.ToString() ?? "";
        
        var startStr = GetValue(startKey)?.ToString() ?? DateTime.UtcNow.ToString("o");
        var endStr = GetValue(endKey)?.ToString() ?? DateTime.UtcNow.AddHours(1).ToString("o");

        if (DateTime.TryParse(startStr, out var startDate))
        {
            startStr = startDate.ToString("yyyy-MM-ddTHH:mm:ss");
        }
        if (DateTime.TryParse(endStr, out var endDate))
        {
            endStr = endDate.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        var location = GetValue(locationKey)?.ToString() ?? "";
        var isAllDayVal = GetValue(isAllDayKey);
        bool isAllDay = false;
        if (isAllDayVal != null)
        {
            if (isAllDayVal is bool b) isAllDay = b;
            else bool.TryParse(isAllDayVal.ToString(), out isAllDay);
        }

        var eventPayload = new Dictionary<string, object>
        {
            { "subject", subject },
            { "body", new { contentType = "HTML", content = body } },
            { "start", new { dateTime = startStr, timeZone = "UTC" } },
            { "end", new { dateTime = endStr, timeZone = "UTC" } },
            { "isAllDay", isAllDay }
        };

        if (!string.IsNullOrEmpty(location))
        {
            eventPayload["location"] = new { displayName = location };
        }

        var recType = GetValue(recTypeKey)?.ToString();
        if (!string.IsNullOrWhiteSpace(recType) && !string.Equals(recType, "none", StringComparison.OrdinalIgnoreCase))
        {
            var intervalStr = GetValue(recIntervalKey)?.ToString() ?? "1";
            int.TryParse(intervalStr, out var interval);
            if (interval <= 0) interval = 1;

            var pattern = new Dictionary<string, object>
            {
                { "type", recType.ToLower() },
                { "interval", interval }
            };

            var daysOfWeekStr = GetValue(recDaysOfWeekKey)?.ToString();
            if (!string.IsNullOrWhiteSpace(daysOfWeekStr))
            {
                var days = daysOfWeekStr.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(d => d.Trim().ToLower())
                                        .ToArray();
                if (days.Length > 0)
                {
                    pattern["daysOfWeek"] = days;
                }
            }

            var rangeType = GetValue(recRangeTypeKey)?.ToString() ?? "noEnd";
            var range = new Dictionary<string, object>
            {
                { "type", rangeType.ToLower() },
                { "startDate", DateTime.TryParse(startStr, out var parsedStart) ? parsedStart.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd") }
            };

            if (string.Equals(rangeType, "endDate", StringComparison.OrdinalIgnoreCase))
            {
                var endRecStr = GetValue(recEndDateKey)?.ToString();
                if (DateTime.TryParse(endRecStr, out var parsedEnd))
                {
                    range["endDate"] = parsedEnd.ToString("yyyy-MM-dd");
                }
                else
                {
                    range["endDate"] = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd");
                }
            }
            else if (string.Equals(rangeType, "numbered", StringComparison.OrdinalIgnoreCase))
            {
                var occStr = GetValue(recOccurrencesKey)?.ToString() ?? "10";
                int.TryParse(occStr, out var occurrences);
                if (occurrences <= 0) occurrences = 10;
                range["numberOfOccurrences"] = occurrences;
            }

            eventPayload["recurrence"] = new
            {
                pattern = pattern,
                range = range
            };
        }

        return eventPayload;
    }

    private Task<List<Dictionary<string, object>>> ReadSqlRowsAsync(Connector sqlConnector)
    {
        throw new NotImplementedException();
    }

    private Task<List<Dictionary<string, object>>> ReadExcelRowsAsync(Connector excelConnector)
    {
        throw new NotImplementedException();
    }
}
