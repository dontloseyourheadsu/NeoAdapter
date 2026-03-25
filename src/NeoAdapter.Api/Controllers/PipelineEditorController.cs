using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeoAdapter.Application.Pipeline;
using NeoAdapter.Contracts.Pipeline;

namespace NeoAdapter.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/pipeline-editor")]
public sealed class PipelineEditorController(IPipelineEditorService pipelineEditorService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PipelineEditorResponse>> Get(CancellationToken cancellationToken)
    {
        var response = await pipelineEditorService.GetEditorAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPut("draft")]
    public async Task<ActionResult<PipelineDraft>> UpdateDraft(
        [FromBody] UpdatePipelineDraftRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await pipelineEditorService.UpdateDraftAsync(request, cancellationToken);
        return Ok(draft);
    }
}
