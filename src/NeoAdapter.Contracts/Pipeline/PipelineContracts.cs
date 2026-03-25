namespace NeoAdapter.Contracts.Pipeline;

public enum ConnectorType
{
    Sql,
    Csv
}

public sealed record ConnectorDefinition(
    ConnectorType Type,
    string DisplayName,
    string Description,
    string BadgeText,
    bool CanBeSource,
    bool CanBeDestination);

public sealed record PipelineDraft(
    ConnectorType SourceConnectorType,
    ConnectorType DestinationConnectorType,
    string Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record PipelineEditorResponse(
    IReadOnlyList<ConnectorDefinition> Connectors,
    PipelineDraft Draft,
    DateTimeOffset GeneratedAtUtc);

public sealed record UpdatePipelineDraftRequest(
    ConnectorType SourceConnectorType,
    ConnectorType DestinationConnectorType);
