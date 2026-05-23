namespace NeoAdapter.Domain;

public sealed class Group
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid OrganizationId { get; set; }

    public Guid CreatorUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Organization? Organization { get; set; }

    public UserAccount? Creator { get; set; }

    public ICollection<UserAccount> Members { get; set; } = new List<UserAccount>();
}
