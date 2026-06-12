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

        var hasGuestReadPermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.UserId == user.Id && (g.CanRead || g.CanEdit), cancellationToken);

        if (!user.RoleRead && !user.RoleAdmin && !hasGuestReadPermission) return Forbid();

        var jobs = await integrationJobService.GetAllAsync(user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleRead, user.RoleAdmin, cancellationToken);
        return Ok(jobs);
    }

    [HttpPost]
    public async Task<ActionResult<IntegrationJobDto>> Create(
        [FromBody] CreateIntegrationJobRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();
        if (!user.RoleCreate && !user.RoleAdmin) return Forbid();

        try
        {
            var created = await integrationJobService.CreateAsync(request, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleCreate, user.RoleAdmin, cancellationToken);
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpPost("{id:guid}/run")]
    public async Task<ActionResult<EnqueueIntegrationJobResponse>> RunNow(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        var hasGuestEditPermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.IntegrationJobId == id && g.UserId == user.Id && g.CanEdit, cancellationToken);

        if (!user.RoleEdit && !user.RoleAdmin && !hasGuestEditPermission) return Forbid();

        try
        {
            var result = await integrationJobService.EnqueueRunAsync(id, user.Username, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleEdit, user.RoleAdmin, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpGet("{id:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<IntegrationJobRunDto>>> GetRuns(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        var hasGuestReadPermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.IntegrationJobId == id && g.UserId == user.Id && (g.CanRead || g.CanEdit), cancellationToken);

        if (!user.RoleRead && !user.RoleAdmin && !hasGuestReadPermission) return Forbid();

        try
        {
            var runs = await integrationJobService.GetJobRunsAsync(id, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleRead, user.RoleAdmin, cancellationToken);
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
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        var hasGuestReadPermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.IntegrationJobId == id && g.UserId == user.Id && (g.CanRead || g.CanEdit), cancellationToken);

        if (!user.RoleRead && !user.RoleAdmin && !hasGuestReadPermission) return Forbid();

        try
        {
            var result = await integrationJobService.GetJobLogsAsync(id, runId, cursor, limit, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleRead, user.RoleAdmin, cancellationToken);
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

    [HttpGet("{id:guid}/guests")]
    public async Task<ActionResult<IReadOnlyList<IntegrationJobGuestDto>>> GetGuests(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            var result = await integrationJobService.GetGuestsAsync(id, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleRead, user.RoleAdmin, cancellationToken);
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

    [HttpPost("{id:guid}/guests")]
    public async Task<IActionResult> InviteGuest(Guid id, [FromBody] InviteGuestRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            await integrationJobService.InviteGuestAsync(id, request, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleEdit, user.RoleAdmin, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpPut("{id:guid}/guests/{guestUserId:guid}")]
    public async Task<IActionResult> UpdateGuestPermissions(Guid id, Guid guestUserId, [FromBody] UpdateGuestPermissionsRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            await integrationJobService.UpdateGuestPermissionsAsync(id, guestUserId, request, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleEdit, user.RoleAdmin, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpDelete("{id:guid}/guests/{guestUserId:guid}")]
    public async Task<IActionResult> RemoveGuest(Guid id, Guid guestUserId, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            await integrationJobService.RemoveGuestAsync(id, guestUserId, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleEdit, user.RoleAdmin, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpGet("{id:guid}/owners")]
    public async Task<ActionResult<IReadOnlyList<IntegrationJobOwnerDto>>> GetOwners(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            var result = await integrationJobService.GetOwnersAsync(id, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleRead, user.RoleAdmin, cancellationToken);
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

    [HttpPost("{id:guid}/owners")]
    public async Task<IActionResult> AddOwner(Guid id, [FromBody] AddOwnerRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            await integrationJobService.AddOwnerAsync(id, request, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleEdit, user.RoleAdmin, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpDelete("{id:guid}/owners/{ownerUserId:guid}")]
    public async Task<IActionResult> RemoveOwner(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            await integrationJobService.RemoveOwnerAsync(id, ownerUserId, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleEdit, user.RoleAdmin, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }

    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id, [FromBody] UnlockJobRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            var success = await integrationJobService.UnlockAsync(id, request.Password, user.Id, cancellationToken);
            if (!success)
            {
                return BadRequest("Invalid password.");
            }
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> UpdatePassword(Guid id, [FromBody] UpdateJobPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        try
        {
            await integrationJobService.UpdatePasswordAsync(id, request.Password, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleEdit, user.RoleAdmin, cancellationToken);
            return NoContent();
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