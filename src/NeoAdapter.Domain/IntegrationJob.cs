namespace NeoAdapter.Domain;

public sealed class IntegrationJob
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? OwnerUserId { get; set; }

    public Guid? OwnerGroupId { get; set; }

    public Guid? OwnerOrganizationId { get; set; }

    public bool IsEnabled { get; set; }

    public string? CronExpression { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<IntegrationJobStep> Steps { get; set; } = new List<IntegrationJobStep>();
}
