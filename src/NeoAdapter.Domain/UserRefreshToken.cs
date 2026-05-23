namespace NeoAdapter.Domain;

public sealed class UserRefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public UserAccount? User { get; set; }

    public string Token { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public bool IsRevoked { get; set; }

    public bool IsUsed { get; set; }
}
