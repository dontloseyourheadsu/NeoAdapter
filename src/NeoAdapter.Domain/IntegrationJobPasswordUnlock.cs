using System;

namespace NeoAdapter.Domain;

public sealed class IntegrationJobPasswordUnlock
{
    public Guid IntegrationJobId { get; set; }
    
    public Guid UserId { get; set; }

    public DateTimeOffset UnlockedAtUtc { get; set; }

    public IntegrationJob? IntegrationJob { get; set; }
    
    public UserAccount? User { get; set; }
}
