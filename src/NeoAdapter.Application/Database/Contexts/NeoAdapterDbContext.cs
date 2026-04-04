using Microsoft.EntityFrameworkCore;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Contexts;

public sealed class NeoAdapterDbContext(DbContextOptions<NeoAdapterDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<Connector> Connectors => Set<Connector>();

    public DbSet<IntegrationJob> IntegrationJobs => Set<IntegrationJob>();

    public DbSet<IntegrationJobRun> IntegrationJobRuns => Set<IntegrationJobRun>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NeoAdapterDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}