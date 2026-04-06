using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/integration-jobs")]
public sealed class IntegrationJobsController(IIntegrationJobService integrationJobService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("ping")]
    public ActionResult<object> Ping()
    {
        return Ok(new
        {
            status = "ok",
            controller = "integration-jobs",
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntegrationJobDto>>> GetAll(CancellationToken cancellationToken)
    {
        var jobs = await integrationJobService.GetAllAsync(cancellationToken);
        return Ok(jobs);
    }

    [HttpPost]
    public async Task<ActionResult<IntegrationJobDto>> Create(
        [FromBody] CreateIntegrationJobRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await integrationJobService.CreateAsync(request, cancellationToken);
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/run")]
    public async Task<ActionResult<EnqueueIntegrationJobResponse>> RunNow(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await integrationJobService.EnqueueRunAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}