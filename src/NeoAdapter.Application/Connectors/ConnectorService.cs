using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Domain;
using ConnectorTypeContract = NeoAdapter.Contracts.Connectors.ConnectorType;
using ConnectorTypeDomain = NeoAdapter.Domain.ConnectorType;

namespace NeoAdapter.Application.Connectors;

public sealed class ConnectorService(
    NeoAdapterDbContext dbContext,
    ISqlSecretProtector sqlSecretProtector) : IConnectorService
{
    public async Task<IReadOnlyList<ConnectorDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var connectors = await dbContext.Connectors
            .OrderBy(connector => connector.Name)
            .ToListAsync(cancellationToken);

        return connectors.Select(MapToDto).ToArray();
    }

    public async Task<ConnectorDto> CreateAsync(CreateConnectorRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trimmedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new InvalidOperationException("Connector name is required.");
        }

        var nameExists = await dbContext.Connectors
            .AnyAsync(connector => connector.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (nameExists)
        {
            throw new InvalidOperationException("A connector with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var connector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Type = request.Type == ConnectorTypeContract.Sql ? ConnectorTypeDomain.Sql : ConnectorTypeDomain.Csv,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        switch (request.Type)
        {
            case ConnectorTypeContract.Sql:
                if (request.Sql is null)
                {
                    throw new InvalidOperationException("SQL connector settings are required.");
                }

                ValidateSqlSettings(request.Sql);
                connector.SqlServer = request.Sql.Server.Trim();
                connector.SqlPort = request.Sql.Port;
                connector.SqlDatabase = request.Sql.Database.Trim();
                connector.SqlUsername = request.Sql.Username.Trim();
                connector.SqlPassword = sqlSecretProtector.Protect(request.Sql.Password);
                connector.SqlTable = request.Sql.Table.Trim();
                connector.SqlTrustServerCertificate = request.Sql.TrustServerCertificate;
                break;

            case ConnectorTypeContract.Csv:
                if (request.Csv is null)
                {
                    throw new InvalidOperationException("CSV connector settings are required.");
                }

                ValidateCsvSettings(request.Csv);
                connector.CsvPath = request.Csv.Path.Trim();
                connector.CsvDelimiter = request.Csv.Delimiter;
                break;

            default:
                throw new InvalidOperationException("Unsupported connector type.");
        }

        dbContext.Connectors.Add(connector);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(connector);
    }

    public async Task<TestConnectorResponse> TestAsync(TestConnectorRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (request.Type)
        {
            case ConnectorTypeContract.Sql:
                if (request.Sql is null)
                {
                    throw new InvalidOperationException("SQL connector settings are required.");
                }

                ValidateSqlSettings(request.Sql);
                await using (var connection = new SqlConnection(BuildSqlConnectionString(request.Sql)))
                {
                    await connection.OpenAsync(cancellationToken);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    await command.ExecuteScalarAsync(cancellationToken);
                }

                return new TestConnectorResponse(true, "SQL connection test succeeded.", DateTimeOffset.UtcNow);

            case ConnectorTypeContract.Csv:
                if (request.Csv is null)
                {
                    throw new InvalidOperationException("CSV connector settings are required.");
                }

                ValidateCsvSettings(request.Csv);
                var path = request.Csv.Path.Trim();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    await stream.FlushAsync(cancellationToken);
                }

                return new TestConnectorResponse(true, "CSV path test succeeded.", DateTimeOffset.UtcNow);

            default:
                throw new InvalidOperationException("Unsupported connector type.");
        }
    }

    private static void ValidateSqlSettings(SqlConnectorSettingsInputDto sql)
    {
        if (string.IsNullOrWhiteSpace(sql.Server)
            || string.IsNullOrWhiteSpace(sql.Database)
            || string.IsNullOrWhiteSpace(sql.Username)
            || string.IsNullOrWhiteSpace(sql.Password)
            || string.IsNullOrWhiteSpace(sql.Table))
        {
            throw new InvalidOperationException("SQL connector requires server, database, username, password, and table.");
        }

        if (sql.Port <= 0)
        {
            throw new InvalidOperationException("SQL connector port must be greater than zero.");
        }
    }

    private static void ValidateCsvSettings(CsvConnectorSettingsDto csv)
    {
        if (string.IsNullOrWhiteSpace(csv.Path))
        {
            throw new InvalidOperationException("CSV connector path is required.");
        }

        if (string.IsNullOrEmpty(csv.Delimiter))
        {
            throw new InvalidOperationException("CSV delimiter is required.");
        }
    }

    private static ConnectorDto MapToDto(Connector connector)
    {
        SqlConnectorSettingsDto? sql = null;
        CsvConnectorSettingsDto? csv = null;

        if (connector.Type == ConnectorTypeDomain.Sql)
        {
            sql = new SqlConnectorSettingsDto(
                connector.SqlServer ?? string.Empty,
                connector.SqlPort ?? 1433,
                connector.SqlDatabase ?? string.Empty,
                connector.SqlUsername ?? string.Empty,
                connector.SqlTable ?? string.Empty,
                connector.SqlTrustServerCertificate);
        }

        if (connector.Type == ConnectorTypeDomain.Csv)
        {
            csv = new CsvConnectorSettingsDto(
                connector.CsvPath ?? string.Empty,
                connector.CsvDelimiter);
        }

        return new ConnectorDto(
            connector.Id,
            connector.Name,
            connector.Type == ConnectorTypeDomain.Sql ? ConnectorTypeContract.Sql : ConnectorTypeContract.Csv,
            sql,
            csv,
            connector.CreatedAtUtc,
            connector.UpdatedAtUtc);
    }

    private static string BuildSqlConnectionString(SqlConnectorSettingsInputDto sql)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{sql.Server},{sql.Port}",
            InitialCatalog = sql.Database,
            UserID = sql.Username,
            Password = sql.Password,
            Encrypt = true,
            TrustServerCertificate = sql.TrustServerCertificate,
            IntegratedSecurity = false,
            ConnectTimeout = 20
        };

        return builder.ConnectionString;
    }
}