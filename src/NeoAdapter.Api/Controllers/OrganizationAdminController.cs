using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NeoAdapter.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/org-admin")]
public sealed class OrganizationAdminController(NeoAdapterDbContext dbContext) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<OrganizationUserDto>>> GetUsers(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        if (!user.RoleAdmin && user.Role != "Admin")
        {
            return Forbid();
        }

        var users = await dbContext.UserAccounts
            .Include(u => u.Group)
            .Where(u => u.OrganizationId == user.OrganizationId)
            .OrderBy(u => u.Username)
            .Select(u => new OrganizationUserDto(
                u.Id,
                u.Username,
                u.GroupId,
                u.Group != null ? u.Group.Name : null,
                u.Role,
                u.RoleRead,
                u.RoleEdit,
                u.RoleCreate,
                u.RoleAdmin
            ))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<OrganizationGroupDto>>> GetGroups(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();


        var groups = await dbContext.Groups
            .Where(g => g.OrganizationId == user.OrganizationId)
            .OrderBy(g => g.Name)
            .Select(g => new OrganizationGroupDto(g.Id, g.Name))
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }

    [HttpPut("users/{userId}/roles")]
    public async Task<IActionResult> UpdateUserRoles(
        Guid userId,
        [FromBody] UpdateUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Unauthorized();

        if (!user.RoleAdmin && user.Role != "Admin")
        {
            return Forbid();
        }

        var targetUser = await dbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == userId && u.OrganizationId == user.OrganizationId, cancellationToken);

        if (targetUser == null)
        {
            return NotFound();
        }

        // Constraints validation:
        // 1. Edit and Create require Read
        if ((request.RoleEdit || request.RoleCreate) && !request.RoleRead)
        {
            return BadRequest("Edit and Create permissions require Read permission.");
        }

        // 2. Create requires Edit
        if (request.RoleCreate && !request.RoleEdit)
        {
            return BadRequest("Create permission requires Edit permission.");
        }

        // 3. Last admin validation
        if (targetUser.RoleAdmin && !request.RoleAdmin)
        {
            var adminCount = await dbContext.UserAccounts
                .CountAsync(u => u.OrganizationId == user.OrganizationId && u.RoleAdmin, cancellationToken);
            if (adminCount <= 1)
            {
                return BadRequest("Cannot demote the last administrator in the organization.");
            }
        }

        // 4. Group validation
        if (request.GroupId.HasValue)
        {
            var groupExists = await dbContext.Groups
                .AnyAsync(g => g.Id == request.GroupId && g.OrganizationId == user.OrganizationId, cancellationToken);
            if (!groupExists)
            {
                return BadRequest("Selected group does not exist in your organization.");
            }
        }

        targetUser.RoleRead = request.RoleRead;
        targetUser.RoleEdit = request.RoleEdit;
        targetUser.RoleCreate = request.RoleCreate;
        targetUser.RoleAdmin = request.RoleAdmin;
        targetUser.Role = request.RoleAdmin ? "Admin" : "User";
        targetUser.GroupId = request.GroupId;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok();
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
