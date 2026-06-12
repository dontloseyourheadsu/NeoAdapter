using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Domain;
using System.Security.Claims;

namespace NeoAdapter.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/connectors")]
public sealed class ConnectorsController(IConnectorService connectorService, NeoAdapterDbContext dbContext) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("ping")]
    public ActionResult<object> Ping()
    {
        return Ok(new
        {
            status = "ok",
            controller = "connectors",
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectorDto>>> GetAll(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();
        if (!user.RoleRead && !user.RoleAdmin) return Forbid();

        var response = await connectorService.GetAllAsync(user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleRead, user.RoleAdmin, cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ConnectorDto>> Create(
        [FromBody] CreateConnectorRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        var hasGuestCreatePermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.UserId == user.Id && g.CanCreateConnectors, cancellationToken);

        if (!user.RoleCreate && !user.RoleAdmin && !hasGuestCreatePermission) return Forbid();

        try
        {
            var connector = await connectorService.CreateAsync(request, user.Id, user.OrganizationId, user.GroupId, user.Role, user.RoleCreate, user.RoleAdmin, cancellationToken);
            return Ok(connector);
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

    [HttpPost("test")]
    public async Task<ActionResult<TestConnectorResponse>> Test(
        [FromBody] TestConnectorRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();
        if (!user.RoleCreate && !user.RoleEdit && !user.RoleAdmin) return Forbid();

        try
        {
            var result = await connectorService.TestAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<UserAccount?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
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