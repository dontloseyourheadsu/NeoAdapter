using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeoAdapter.Application.Dashboard;
using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken)
    {
        var response = await dashboardService.GetDashboardAsync(cancellationToken);
        return Ok(response);
    }
}