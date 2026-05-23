namespace NeoAdapter.Domain;

public sealed class IntegrationJobStep
{
    public Guid Id { get; set; }

    public Guid IntegrationJobId { get; set; }

    public int OrderIndex { get; set; }

    public Guid SourceConnectorId { get; set; }

    public Guid DestinationConnectorId { get; set; }

    public IntegrationJob? IntegrationJob { get; set; }

    public Connector? SourceConnector { get; set; }

    public Connector? DestinationConnector { get; set; }
}
