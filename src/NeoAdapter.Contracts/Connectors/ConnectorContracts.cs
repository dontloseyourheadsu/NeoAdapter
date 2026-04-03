namespace NeoAdapter.Contracts.Connectors;

public enum ConnectorType
{
    Sql,
    Csv
}

public sealed record ConnectorDto(
    Guid Id,
    string Name,
    ConnectorType Type,
    SqlConnectorSettingsDto? Sql,
    CsvConnectorSettingsDto? Csv,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SqlConnectorSettingsDto(
    string Server,
    int Port,
    string Database,
    string Username,
    string Table,
    bool TrustServerCertificate);

public sealed record CsvConnectorSettingsDto(
    string Path,
    string Delimiter);

public sealed record CreateConnectorRequest(
    string Name,
    ConnectorType Type,
    SqlConnectorSettingsDto? Sql,
    CsvConnectorSettingsDto? Csv);