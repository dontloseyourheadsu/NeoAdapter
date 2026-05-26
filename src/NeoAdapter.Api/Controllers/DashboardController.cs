using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Dashboard;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Dashboard;
using NeoAdapter.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NeoAdapter.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService, NeoAdapterDbContext dbContext) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("ping")]
    public ActionResult<object> Ping()
    {
        return Ok(new
        {
            status = "ok",
            controller = "dashboard",
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(
        [FromQuery] DashboardFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        // Enforce tenancy filter:
        var finalFilter = new DashboardFilterRequest(
            user.OrganizationId,
            filter.GroupId ?? user.GroupId,
            filter.UserId ?? user.Id
        );

        if (user.Role != "Admin")
        {
            finalFilter = new DashboardFilterRequest(
                user.OrganizationId,
                user.GroupId,
                user.Id
            );
        }

        var response = await dashboardService.GetDashboardAsync(finalFilter, cancellationToken);
        return Ok(response);
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