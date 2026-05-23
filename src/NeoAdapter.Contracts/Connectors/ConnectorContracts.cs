namespace NeoAdapter.Contracts.Connectors;

public enum ConnectorType
{
    SqlServer,
    Postgres,
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
    string Host,
    int Port,
    string Database,
    string Username,
    bool TrustServerCertificate,
    string? ConfigJson);

public sealed record SqlConnectorSettingsInputDto(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate,
    string? ConfigJson = null);

public sealed record CsvConnectorSettingsDto(
    string Path,
    string Delimiter);

public sealed record CreateConnectorRequest(
    string Name,
    ConnectorType Type,
    SqlConnectorSettingsInputDto? Sql,
    CsvConnectorSettingsDto? Csv);

public sealed record TestConnectorRequest(
    ConnectorType Type,
    SqlConnectorSettingsInputDto? Sql,
    CsvConnectorSettingsDto? Csv);

public sealed record TestConnectorResponse(
    bool IsSuccess,
    string Message,
    DateTimeOffset TestedAtUtc);
