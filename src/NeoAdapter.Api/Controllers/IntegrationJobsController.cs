using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Domain;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NeoAdapter.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/integration-jobs")]
public sealed class IntegrationJobsController(IIntegrationJobService integrationJobService, NeoAdapterDbContext dbContext) : ControllerBase
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
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        var jobs = await integrationJobService.GetAllAsync(user.Id, user.OrganizationId, user.GroupId, user.Role, cancellationToken);
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
            var user = await GetCurrentUserAsync(cancellationToken);
            if (user == null) return Unauthorized();

            var result = await integrationJobService.EnqueueRunAsync(id, user.Username, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<IntegrationJobRunDto>>> GetRuns(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var user = await GetCurrentUserAsync(cancellationToken);
            if (user == null) return Unauthorized();

            var runs = await integrationJobService.GetJobRunsAsync(id, user.Id, user.OrganizationId, user.GroupId, user.Role, cancellationToken);
            return Ok(runs);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpGet("{id:guid}/logs")]
    public async Task<ActionResult<JobLogsResponse>> GetLogs(
        Guid id,
        [FromQuery] Guid? runId,
        [FromQuery] DateTimeOffset? cursor,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await GetCurrentUserAsync(cancellationToken);
            if (user == null) return Unauthorized();

            var result = await integrationJobService.GetJobLogsAsync(id, runId, cursor, limit, user.Id, user.OrganizationId, user.GroupId, user.Role, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    private async Task<UserAccount?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return await dbContext.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }
}