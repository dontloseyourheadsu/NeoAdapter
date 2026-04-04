using System.Data;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;

namespace NeoAdapter.Application.IntegrationJobs;

public sealed class IntegrationJobExecutor(
    NeoAdapterDbContext dbContext,
    ISqlSecretProtector sqlSecretProtector) : IIntegrationJobExecutor
{
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
            ?? new Domain.IntegrationJobRun
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
            if (source.Type == Domain.ConnectorType.Sql && destination.Type == Domain.ConnectorType.Csv)
            {
                processed = await ExecuteSqlToCsvAsync(source, destination);
            }
            else if (source.Type == Domain.ConnectorType.Csv && destination.Type == Domain.ConnectorType.Sql)
            {
                processed = await ExecuteCsvToSqlAsync(source, destination);
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

    private async Task<int> ExecuteSqlToCsvAsync(Domain.Connector sqlConnector, Domain.Connector csvConnector)
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

        await using var sqlConnection = new SqlConnection(BuildSqlConnectionString(sqlConnector));
        await sqlConnection.OpenAsync();

        var table = sqlConnector.SqlTable ?? throw new InvalidOperationException("SQL source table is required.");
        await using var command = sqlConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM [{table}]";

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

    private async Task<int> ExecuteCsvToSqlAsync(Domain.Connector csvConnector, Domain.Connector sqlConnector)
    {
        var csvPath = csvConnector.CsvPath;
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            throw new InvalidOperationException("CSV source path does not exist.");
        }

        await using var sqlConnection = new SqlConnection(BuildSqlConnectionString(sqlConnector));
        await sqlConnection.OpenAsync();

        var table = sqlConnector.SqlTable ?? throw new InvalidOperationException("SQL destination table is required.");
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

        var quotedColumns = string.Join(",", headers.Select(header => $"[{header}]"));
        var parameterNames = headers.Select((_, index) => $"@p{index}").ToArray();
        var parametersSql = string.Join(",", parameterNames);
        var commandText = $"INSERT INTO [{table}] ({quotedColumns}) VALUES ({parametersSql})";

        var inserted = 0;
        while (await csvReader.ReadAsync())
        {
            await using var insertCommand = sqlConnection.CreateCommand();
            insertCommand.CommandText = commandText;
            for (var index = 0; index < headers.Length; index++)
            {
                var value = csvReader.GetField(index);
                insertCommand.Parameters.Add(new SqlParameter(parameterNames[index], value ?? (object)DBNull.Value));
            }

            await insertCommand.ExecuteNonQueryAsync();
            inserted++;
        }

        return inserted;
    }

    private string BuildSqlConnectionString(Domain.Connector connector)
    {
        if (connector.Type != Domain.ConnectorType.Sql)
        {
            throw new InvalidOperationException("Expected SQL connector.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{connector.SqlServer},{connector.SqlPort ?? 1433}",
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
}