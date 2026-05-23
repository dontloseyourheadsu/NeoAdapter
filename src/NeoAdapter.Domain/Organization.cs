namespace NeoAdapter.Domain;

public sealed class Organization
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<UserAccount> Users { get; set; } = new List<UserAccount>();

    public ICollection<Group> Groups { get; set; } = new List<Group>();
}
