using System;

namespace NeoAdapter.Domain;

public sealed class IntegrationJobLogEntry
{
    public Guid Id { get; set; }

    public Guid IntegrationJobId { get; set; }

    public Guid? IntegrationJobRunId { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string LogLevel { get; set; } = string.Empty; // "INFO", "WARN", "ERROR"

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public IntegrationJob? IntegrationJob { get; set; }

    public IntegrationJobRun? IntegrationJobRun { get; set; }
}
