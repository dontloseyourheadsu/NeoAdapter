using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/connectors")]
public sealed class ConnectorsController(IConnectorService connectorService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectorDto>>> GetAll(CancellationToken cancellationToken)
    {
        var response = await connectorService.GetAllAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ConnectorDto>> Create(
        [FromBody] CreateConnectorRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var connector = await connectorService.CreateAsync(request, cancellationToken);
            return Ok(connector);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}