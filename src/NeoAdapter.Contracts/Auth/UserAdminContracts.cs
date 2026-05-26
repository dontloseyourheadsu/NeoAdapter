namespace NeoAdapter.Contracts.Auth;

public sealed record OrganizationUserDto(
    Guid Id,
    string Username,
    Guid? GroupId,
    string? GroupName,
    string Role,
    bool RoleRead,
    bool RoleEdit,
    bool RoleCreate,
    bool RoleAdmin);

public sealed record OrganizationGroupDto(
    Guid Id,
    string Name);

public sealed record UpdateUserRolesRequest(
    bool RoleRead,
    bool RoleEdit,
    bool RoleCreate,
    bool RoleAdmin,
    Guid? GroupId);
