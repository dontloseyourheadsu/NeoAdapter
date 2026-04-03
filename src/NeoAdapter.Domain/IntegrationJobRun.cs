namespace NeoAdapter.Domain;

public sealed class IntegrationJobRun
{
    public Guid Id { get; set; }

    public Guid IntegrationJobId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public int RecordsProcessed { get; set; }

    public string? HangfireJobId { get; set; }

    public IntegrationJob? IntegrationJob { get; set; }
}