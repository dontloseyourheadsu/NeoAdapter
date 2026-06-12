using System;

namespace NeoAdapter.Domain;

public sealed class IntegrationJobGuest
{
    public Guid IntegrationJobId { get; set; }
    
    public Guid UserId { get; set; }

    public bool CanRead { get; set; }
    
    public bool CanEdit { get; set; }
    
    public bool CanCreateConnectors { get; set; }

    public IntegrationJob? IntegrationJob { get; set; }
    
    public UserAccount? User { get; set; }
}
