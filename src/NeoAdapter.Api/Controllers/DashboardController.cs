using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeoAdapter.Application.Dashboard;
using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
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
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken)
    {
        var response = await dashboardService.GetDashboardAsync(cancellationToken);
        return Ok(response);
    }
}