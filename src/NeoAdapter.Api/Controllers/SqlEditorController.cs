using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.SqlEditor;
using NeoAdapter.Contracts.SqlEditor;
using NeoAdapter.Domain;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace NeoAdapter.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/sqleditor")]
public sealed class SqlEditorController(ISqlEditorService sqlEditorService, NeoAdapterDbContext dbContext) : ControllerBase
{
    [HttpPost("schema")]
    public async Task<ActionResult<SqlSchemaResponse>> GetSchema(
        [FromBody] GetSchemaRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        if (!user.RoleRead && !user.RoleAdmin) return Forbid();

        try
        {
            var response = await sqlEditorService.GetSchemaAsync(
                request,
                user.Id,
                user.OrganizationId,
                user.GroupId,
                user.Role,
                user.RoleRead,
                user.RoleCreate,
                user.RoleAdmin,
                cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("query")]
    public async Task<ActionResult<QueryResultDto>> ExecuteQuery(
        [FromBody] ExecuteQueryRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        if (!user.RoleRead && !user.RoleAdmin) return Forbid();

        try
        {
            var response = await sqlEditorService.ExecuteQueryAsync(
                request,
                user.Id,
                user.OrganizationId,
                user.GroupId,
                user.Role,
                user.RoleRead,
                user.RoleCreate,
                user.RoleAdmin,
                cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
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
