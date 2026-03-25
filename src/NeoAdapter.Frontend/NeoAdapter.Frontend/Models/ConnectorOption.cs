using NeoAdapter.Contracts.Pipeline;

namespace NeoAdapter.Frontend.Models;

public sealed record ConnectorOption(
    ConnectorType Type,
    string DisplayName,
    string Description,
    string BadgeText);
