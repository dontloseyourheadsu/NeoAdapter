using Microsoft.EntityFrameworkCore;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Contexts;

public sealed class NeoAdapterDbContext(DbContextOptions<NeoAdapterDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<Connector> Connectors => Set<Connector>();

    public DbSet<IntegrationJob> IntegrationJobs => Set<IntegrationJob>();

    public DbSet<IntegrationJobGuest> IntegrationJobGuests => Set<IntegrationJobGuest>();

    public DbSet<IntegrationJobPasswordUnlock> IntegrationJobPasswordUnlocks => Set<IntegrationJobPasswordUnlock>();

    public DbSet<IntegrationJobRun> IntegrationJobRuns => Set<IntegrationJobRun>();

    public DbSet<IntegrationJobLogEntry> IntegrationJobLogs => Set<IntegrationJobLogEntry>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Group> Groups => Set<Group>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NeoAdapterDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}