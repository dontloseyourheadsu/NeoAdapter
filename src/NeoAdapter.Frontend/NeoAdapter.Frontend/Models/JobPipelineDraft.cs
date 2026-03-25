using System;
using NeoAdapter.Contracts.Pipeline;

namespace NeoAdapter.Frontend.Models;

public sealed class JobPipelineDraft
{
    public ConnectorType SourceConnectorType { get; set; }

    public ConnectorType DestinationConnectorType { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
