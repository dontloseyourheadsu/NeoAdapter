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
            .Include(item => item.SourceConnector)
            .Include(item => item.DestinationConnector)
            .FirstOrDefaultAsync(item => item.Id == integrationJobId)
            ?? throw new InvalidOperationException("Integration job was not found.");

        var run = await dbContext.IntegrationJobRuns
            .Where(item => item.IntegrationJobId == integrationJobId)
            .OrderByDescending(item => item.StartedAtUtc)
            .FirstOrDefaultAsync(item => item.Status == "QUEUED")
            ?? new IntegrationJobRun
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = integrationJobId,
                Status = "QUEUED",
                Message = "Queued for execution.",
                StartedAtUtc = DateTimeOffset.UtcNow,
                RecordsProcessed = 0
            };

        var isTracked = await dbContext.IntegrationJobRuns.AnyAsync(item => item.Id == run.Id);
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
            var source = job.SourceConnector ?? throw new InvalidOperationException("Source connector is missing.");
            var destination = job.DestinationConnector ?? throw new InvalidOperationException("Destination connector is missing.");

            int processed;
            if (IsSqlConnector(source.Type) && destination.Type == ConnectorType.Csv)
            {
                processed = await ExecuteSqlToCsvAsync(source, destination);
            }
            else if (source.Type == ConnectorType.Csv && IsSqlConnector(destination.Type))
            {
                processed = await ExecuteCsvToSqlAsync(source, destination);
            }
            else if (IsSqlConnector(source.Type) && IsSqlConnector(destination.Type))
            {
                processed = await ExecuteSqlToSqlAsync(source, destination);
            }
            else
            {
                throw new InvalidOperationException("Unsupported connector direction.");
            }

            run.Status = "SUCCEEDED";
            run.Message = $"Successfully processed {processed} record(s).";
            run.RecordsProcessed = processed;
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
        var configJson = source.SqlConfigJson ?? destination.SqlConfigJson;
        if (string.IsNullOrWhiteSpace(configJson))
        {
            throw new InvalidOperationException("SQL to SQL migration requires configuration (tables and fields).");
        }

        var config = JsonSerializer.Deserialize<SqlConfig>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (config == null || config.Tables.Count == 0)
        {
            return 0;
        }

        await using var sourceConn = await CreateAndOpenSqlConnectionAsync(source);
        await using var destConn = await CreateAndOpenSqlConnectionAsync(destination);

        int totalProcessed = 0;
        foreach (var table in config.Tables)
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

        throw new InvalidOperationException($"Target table '{tableConfig.Name}' does not exist on destination. Please create it or use Auto-Sync (TBD).");
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

        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);
        
        var table = "UnknownTable"; // Placeholder

        await using var command = sqlConnection.CreateCommand();
        command.CommandText = sqlConnector.Type == ConnectorType.SqlServer 
            ? $"SELECT * FROM [{table}]" 
            : $"SELECT * FROM \"{table}\"";

        await using var reader = await command.ExecuteReaderAsync();
        await using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);
        await using var csvWriter = new CsvWriter(writer, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            Delimiter = csvConnector.CsvDelimiter
        });

        for (var index = 0; index < reader.FieldCount; index++)
        {
            csvWriter.WriteField(reader.GetName(index));
        }

        await csvWriter.NextRecordAsync();

        var count = 0;
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                csvWriter.WriteField(reader[index]);
            }

            await csvWriter.NextRecordAsync();
            count++;
        }

        return count;
    }

    private async Task<int> ExecuteCsvToSqlAsync(Connector csvConnector, Connector sqlConnector)
    {
        var csvPath = csvConnector.CsvPath;
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            throw new InvalidOperationException("CSV source path does not exist.");
        }

        await using var sqlConnection = await CreateAndOpenSqlConnectionAsync(sqlConnector);

        var table = "UnknownTable"; // Placeholder
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

        string quotedColumns, parametersSql;
        if (sqlConnector.Type == ConnectorType.SqlServer)
        {
            quotedColumns = string.Join(",", headers.Select(header => $"[{header}]"));
            parametersSql = string.Join(",", headers.Select((_, index) => $"@p{index}"));
        }
        else
        {
            quotedColumns = string.Join(",", headers.Select(header => $"\"{header}\""));
            parametersSql = string.Join(",", headers.Select((_, index) => $"@p{index}"));
        }
        
        var commandText = $"INSERT INTO [{table}] ({quotedColumns}) VALUES ({parametersSql})";
        if (sqlConnector.Type == ConnectorType.Postgres)
        {
            commandText = $"INSERT INTO \"{table}\" ({quotedColumns}) VALUES ({parametersSql})";
        }

        var inserted = 0;
        while (await csvReader.ReadAsync())
        {
            await using var insertCommand = sqlConnection.CreateCommand();
            insertCommand.CommandText = commandText;
            for (var index = 0; index < headers.Length; index++)
            {
                var value = csvReader.GetField(index);
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
}
