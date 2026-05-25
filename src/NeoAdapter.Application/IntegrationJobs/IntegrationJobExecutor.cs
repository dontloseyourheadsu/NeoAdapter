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
                RecordsProcessed = 0
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
            int totalProcessed = 0;
            var steps = job.Steps.OrderBy(s => s.OrderIndex).ToList();
            
            foreach (var step in steps)
            {
                var source = step.SourceConnector ?? throw new InvalidOperationException($"Source connector is missing for step {step.OrderIndex}.");
                var destination = step.DestinationConnector ?? throw new InvalidOperationException($"Destination connector is missing for step {step.OrderIndex}.");

                int processed;
                if (IsSqlConnector(source.Type) && destination.Type == ConnectorType.Csv)
                {
                    processed = await ExecuteSqlToCsvAsync(source, destination);
                }
                else if (source.Type == ConnectorType.Csv && IsSqlConnector(destination.Type))
                {
                    processed = await ExecuteCsvToSqlAsync(source, destination);
                }
                else if (IsSqlConnector(source.Type) && destination.Type == ConnectorType.Excel)
                {
                    processed = await ExecuteSqlToExcelAsync(source, destination);
                }
                else if (source.Type == ConnectorType.Excel && IsSqlConnector(destination.Type))
                {
                    processed = await ExecuteExcelToSqlAsync(source, destination);
                }
                else if (IsSqlConnector(source.Type) && IsSqlConnector(destination.Type))
                {
                    processed = await ExecuteSqlToSqlAsync(source, destination);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported connector direction in step {step.OrderIndex}.");
                }
                
                totalProcessed += processed;
            }

            run.Status = "SUCCEEDED";
            run.Message = $"Successfully processed {totalProcessed} record(s) across {steps.Count} step(s).";
            run.RecordsProcessed = totalProcessed;
            run.FinishedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            run.Status = "FAILED";
            run.Message = ex.Message;
            run.FinishedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
            throw;
        }
    }

    private static bool IsSqlConnector(ConnectorType type) =>
        type == ConnectorType.SqlServer || type == ConnectorType.Postgres;

    private async Task<int> ExecuteSqlToSqlAsync(Connector source, Connector destination)
    {
        var sourceConfig = GetSqlConfig(source);
        
        await using var sourceConn = await CreateAndOpenSqlConnectionAsync(source);
        await using var destConn = await CreateAndOpenSqlConnectionAsync(destination);

        int totalProcessed = 0;
        foreach (var table in sourceConfig.Tables)
        {
            totalProcessed += await TransferTableAsync(sourceConn, destConn, source.Type, destination.Type, table);
        }

        return totalProcessed;
    }

    private async Task<int> TransferTableAsync(
        DbConnection sourceConn, 
        DbConnection destConn, 
        ConnectorType sourceType, 
        ConnectorType destType, 
        SqlTableConfig tableConfig)
    {
        await EnsureTargetTableExistsAsync(sourceConn, destConn, sourceType, destType, tableConfig);

        var fields = string.Join(", ", tableConfig.Fields.Select(f => QuoteIdentifier(sourceType, f)));
        var sourceTable = QuoteIdentifier(sourceType, tableConfig.Name);
        
        await using var selectCmd = sourceConn.CreateCommand();
        selectCmd.CommandText = $"SELECT {fields} FROM {sourceTable}";
        
        await using var reader = await selectCmd.ExecuteReaderAsync();
        
        var destFields = string.Join(", ", tableConfig.Fields.Select(f => QuoteIdentifier(destType, f)));
        var placeholders = string.Join(", ", tableConfig.Fields.Select((_, i) => $"@p{i}"));
        var destTable = QuoteIdentifier(destType, tableConfig.Name);
        
        var insertSql = $"INSERT INTO {destTable} ({destFields}) VALUES ({placeholders})";
        
        int count = 0;
        while (await reader.ReadAsync())
        {
            await using var insertCmd = destConn.CreateCommand();
            insertCmd.CommandText = insertSql;
            for (int i = 0; i < tableConfig.Fields.Count; i++)
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
        SqlTableConfig tableConfig)
    {
        bool exists = await CheckTableExistsAsync(destConn, destType, tableConfig.Name);
        if (exists) return;

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

    private async Task<int> ExecuteSqlToCsvAsync(Connector sqlConnector, Connector csvConnector)
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
        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        
        int totalProcessed = 0;
        foreach (var tableConfig in sqlConfig.Tables)
        {
            var fields = string.Join(", ", tableConfig.Fields.Select(f => QuoteIdentifier(sqlConnector.Type, f)));
            var sourceTable = QuoteIdentifier(sqlConnector.Type, tableConfig.Name);

            await using var command = sqlConnection.CreateCommand();
            command.CommandText = $"SELECT {fields} FROM {sourceTable}";

            await using var reader = await command.ExecuteReaderAsync();
            
            // For multi-table to CSV, we might want separate files or append. 
            // For now, let's just use the path as-is, which might overwrite if multiple tables are selected.
            // A better way would be one file per table: path_TableName.csv
            var tableCsvPath = sqlConfig.Tables.Count > 1 
                ? Path.Combine(directory!, $"{Path.GetFileNameWithoutExtension(csvPath)}_{tableConfig.Name}{Path.GetExtension(csvPath)}")
                : csvPath;

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

            while (await reader.ReadAsync())
            {
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    csvWriter.WriteField(reader[index]);
                }

                await csvWriter.NextRecordAsync();
                totalProcessed++;
            }
        }

        return totalProcessed;
    }

    private async Task<int> ExecuteCsvToSqlAsync(Connector csvConnector, Connector sqlConnector)
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
        
        // For CSV -> SQL, we assume the CSV maps to the first table in the config for now.
        var tableConfig = sqlConfig.Tables[0];
        var table = tableConfig.Name;

        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
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
            return 0;
        }

        var headers = csvReader.HeaderRecord ?? [];
        if (headers.Length == 0)
        {
            return 0;
        }

        // Only insert fields that are both in the CSV and in the SQL config
        var commonFields = headers.Intersect(tableConfig.Fields, StringComparer.OrdinalIgnoreCase).ToList();
        if (commonFields.Count == 0)
        {
            throw new InvalidOperationException($"No matching fields found between CSV and SQL table '{table}'.");
        }

        string quotedColumns = string.Join(",", commonFields.Select(f => QuoteIdentifier(sqlConnector.Type, f)));
        string parametersSql = string.Join(",", commonFields.Select((_, index) => $"@p{index}"));
        
        var commandText = $"INSERT INTO {QuoteIdentifier(sqlConnector.Type, table)} ({quotedColumns}) VALUES ({parametersSql})";

        var inserted = 0;
        while (await csvReader.ReadAsync())
        {
            await using var insertCommand = sqlConnection.CreateCommand();
            insertCommand.CommandText = commandText;
            for (var index = 0; index < commonFields.Count; index++)
            {
                var fieldName = commonFields[index];
                var value = csvReader.GetField(fieldName);
                var param = insertCommand.CreateParameter();
                param.ParameterName = $"@p{index}";
                param.Value = value ?? (object)DBNull.Value;
                insertCommand.Parameters.Add(param);
            }

            await insertCommand.ExecuteNonQueryAsync();
            inserted++;
        }

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

    private async Task<int> ExecuteSqlToExcelAsync(Connector sqlConnector, Connector excelConnector)
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
        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        
        int totalProcessed = 0;
        foreach (var tableConfig in sqlConfig.Tables)
        {
            var fields = string.Join(", ", tableConfig.Fields.Select(f => QuoteIdentifier(sqlConnector.Type, f)));
            var sourceTable = QuoteIdentifier(sqlConnector.Type, tableConfig.Name);

            await using var command = sqlConnection.CreateCommand();
            command.CommandText = $"SELECT {fields} FROM {sourceTable}";

            await using var reader = await command.ExecuteReaderAsync();
            
            var tableExcelPath = sqlConfig.Tables.Count > 1 
                ? Path.Combine(directory!, $"{Path.GetFileNameWithoutExtension(excelPath)}_{tableConfig.Name}{Path.GetExtension(excelPath)}")
                : excelPath;

            var sheetName = excelConnector.ExcelSheetName ?? tableConfig.Name;
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                sheetName = "Sheet1";
            }

            if (File.Exists(tableExcelPath))
            {
                File.Delete(tableExcelPath);
            }

            await MiniExcel.SaveAsAsync(tableExcelPath, reader, sheetName: sheetName);

            await using var countCmd = sqlConnection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {sourceTable}";
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            totalProcessed += count;
        }

        return totalProcessed;
    }

    private async Task<int> ExecuteExcelToSqlAsync(Connector excelConnector, Connector sqlConnector)
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

        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        var sheetName = excelConnector.ExcelSheetName;
        
        var rowsEnumerable = await MiniExcel.QueryAsync(excelPath, useHeaderRow: true, sheetName: sheetName);
        var rows = rowsEnumerable.Cast<IDictionary<string, object>>().ToList();

        if (rows.Count == 0)
        {
            return 0;
        }

        var headers = rows[0].Keys.ToList();
        var commonFields = headers.Intersect(tableConfig.Fields, StringComparer.OrdinalIgnoreCase).ToList();
        if (commonFields.Count == 0)
        {
            throw new InvalidOperationException($"No matching fields found between Excel sheet and SQL table '{table}'.");
        }

        string quotedColumns = string.Join(",", commonFields.Select(f => QuoteIdentifier(sqlConnector.Type, f)));
        string parametersSql = string.Join(",", commonFields.Select((_, index) => $"@p{index}"));
        
        var commandText = $"INSERT INTO {QuoteIdentifier(sqlConnector.Type, table)} ({quotedColumns}) VALUES ({parametersSql})";

        var inserted = 0;
        foreach (var row in rows)
        {
            await using var insertCommand = sqlConnection.CreateCommand();
            insertCommand.CommandText = commandText;
            for (var index = 0; index < commonFields.Count; index++)
            {
                var fieldName = commonFields[index];
                var key = row.Keys.FirstOrDefault(k => string.Equals(k, fieldName, StringComparison.OrdinalIgnoreCase));
                var value = key != null ? row[key] : null;

                var param = insertCommand.CreateParameter();
                param.ParameterName = $"@p{index}";
                param.Value = value ?? DBNull.Value;
                insertCommand.Parameters.Add(param);
            }

            await insertCommand.ExecuteNonQueryAsync();
            inserted++;
        }

        return inserted;
    }
}
