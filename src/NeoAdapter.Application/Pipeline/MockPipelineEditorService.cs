using NeoAdapter.Contracts.Pipeline;

namespace NeoAdapter.Application.Pipeline;

public sealed class MockPipelineEditorService : IPipelineEditorService
{
    private static readonly IReadOnlyList<ConnectorDefinition> SupportedConnectors =
    [
        new(ConnectorType.Sql, "SQL", "Relational database connector", "DB", true, true),
        new(ConnectorType.Csv, "CSV", "Delimited flat-file connector", "CSV", true, true)
    ];

    private readonly Lock _draftLock = new();

    private PipelineDraft _draft = new(
        ConnectorType.Sql,
        ConnectorType.Csv,
        "SQL -> CSV ready",
        DateTimeOffset.UtcNow);

    public Task<PipelineEditorResponse> GetEditorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_draftLock)
        {
            var response = new PipelineEditorResponse(
                SupportedConnectors,
                _draft,
                DateTimeOffset.UtcNow);

            return Task.FromResult(response);
        }
    }

    public Task<PipelineDraft> UpdateDraftAsync(UpdatePipelineDraftRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = $"{request.SourceConnectorType.ToString().ToUpperInvariant()} -> {request.DestinationConnectorType.ToString().ToUpperInvariant()} ready";

        lock (_draftLock)
        {
            _draft = new PipelineDraft(
                request.SourceConnectorType,
                request.DestinationConnectorType,
                status,
                DateTimeOffset.UtcNow);

            return Task.FromResult(_draft);
        }
    }
}
