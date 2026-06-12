namespace NeoAdapter.Domain;

public sealed class IntegrationJob
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? OwnerUserId { get; set; }

    public Guid? OwnerGroupId { get; set; }

    public Guid? OwnerOrganizationId { get; set; }

    public Guid? CreatorUserId { get; set; }

    public bool IsEnabled { get; set; }

    public string? CronExpression { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<IntegrationJobStep> Steps { get; set; } = new List<IntegrationJobStep>();

    public ICollection<Group> Groups { get; set; } = new List<Group>();

    public ICollection<UserAccount> Owners { get; set; } = new List<UserAccount>();

    public ICollection<IntegrationJobGuest> Guests { get; set; } = new List<IntegrationJobGuest>();
}

