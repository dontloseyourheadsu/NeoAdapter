using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Domain;

public sealed class Connector
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ConnectorType Type { get; set; }

    public string? SqlServer { get; set; }

    public int? SqlPort { get; set; }

    public string? SqlDatabase { get; set; }

    public string? SqlUsername { get; set; }

    public string? SqlPassword { get; set; }

    public string? SqlTable { get; set; }

    public bool SqlTrustServerCertificate { get; set; }

    public string? CsvPath { get; set; }

    public string CsvDelimiter { get; set; } = ",";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}