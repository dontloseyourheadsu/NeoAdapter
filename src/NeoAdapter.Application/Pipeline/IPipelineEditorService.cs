using NeoAdapter.Contracts.Pipeline;

namespace NeoAdapter.Application.Pipeline;

public interface IPipelineEditorService
{
    Task<PipelineEditorResponse> GetEditorAsync(CancellationToken cancellationToken);

    Task<PipelineDraft> UpdateDraftAsync(UpdatePipelineDraftRequest request, CancellationToken cancellationToken);
}
