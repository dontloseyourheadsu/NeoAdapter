namespace NeoAdapter.Domain;

public sealed class IntegrationJob
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid SourceConnectorId { get; set; }

    public Guid DestinationConnectorId { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Connector? SourceConnector { get; set; }

    public Connector? DestinationConnector { get; set; }
}