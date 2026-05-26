namespace NeoAdapter.Domain;

public sealed class UserAccount
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public string? GoogleId { get; set; }

    public string? Email { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? GroupId { get; set; }

    public string Role { get; set; } = "User"; // "Admin" or "User"

    public bool RoleRead { get; set; } = true;

    public bool RoleEdit { get; set; } = true;

    public bool RoleCreate { get; set; } = true;

    public bool RoleAdmin { get; set; } = false;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public Organization? Organization { get; set; }

    public Group? Group { get; set; }
}