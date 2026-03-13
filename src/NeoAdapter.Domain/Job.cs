using System.Text.Json;

namespace NeoAdapter.Domain;

public sealed class Job
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public JsonDocument Schedule { get; set; } = JsonDocument.Parse("[]");

    public DateTime CreatedDate { get; set; }

    public DateTime UpdatedDate { get; set; }
}