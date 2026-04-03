using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Connectors;

public sealed class ConnectorService(NeoAdapterDbContext dbContext) : IConnectorService
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
            Type = request.Type,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        switch (request.Type)
        {
            case ConnectorType.Sql:
                if (request.Sql is null)
                {
                    throw new InvalidOperationException("SQL connector settings are required.");
                }

                ValidateSqlSettings(request.Sql);
                connector.SqlServer = request.Sql.Server.Trim();
                connector.SqlPort = request.Sql.Port;
                connector.SqlDatabase = request.Sql.Database.Trim();
                connector.SqlUsername = request.Sql.Username.Trim();
                connector.SqlPassword = request.Sql.Password;
                connector.SqlTable = request.Sql.Table.Trim();
                connector.SqlTrustServerCertificate = request.Sql.TrustServerCertificate;
                break;

            case ConnectorType.Csv:
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

    private static void ValidateSqlSettings(SqlConnectorSettingsDto sql)
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

        if (connector.Type == ConnectorType.Sql)
        {
            sql = new SqlConnectorSettingsDto(
                connector.SqlServer ?? string.Empty,
                connector.SqlPort ?? 1433,
                connector.SqlDatabase ?? string.Empty,
                connector.SqlUsername ?? string.Empty,
                connector.SqlTable ?? string.Empty,
                connector.SqlTrustServerCertificate);
        }

        if (connector.Type == ConnectorType.Csv)
        {
            csv = new CsvConnectorSettingsDto(
                connector.CsvPath ?? string.Empty,
                connector.CsvDelimiter);
        }

        return new ConnectorDto(
            connector.Id,
            connector.Name,
            connector.Type,
            sql,
            csv,
            connector.CreatedAtUtc,
            connector.UpdatedAtUtc);
    }
}