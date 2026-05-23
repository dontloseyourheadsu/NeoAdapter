using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database;

public static class NeoAdapterSeedData
{
    public static async Task SeedAsync(
        NeoAdapterDbContext dbContext,
        ISqlSecretProtector sqlSecretProtector,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Ensure at least one Organization exists
        var org = await dbContext.Organizations.FirstOrDefaultAsync(cancellationToken);
        if (org == null)
        {
            org = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "NeoAdapter Default Org",
                CreatedAtUtc = now
            };
            dbContext.Organizations.Add(org);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // 2. Ensure Admin User exists
        var adminExists = await dbContext.UserAccounts.AnyAsync(u => u.Username.ToLower() == "admin", cancellationToken);
        if (!adminExists)
        {
            var salt = Guid.NewGuid().ToString();
            var admin = new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                PasswordSalt = salt,
                PasswordHash = passwordHasher.HashPassword("Admin123!").Hash,
                OrganizationId = org.Id,
                Role = "Admin",
                CreatedAtUtc = now
            };
            dbContext.UserAccounts.Add(admin);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // 3. Fix existing users who might be missing an OrganizationId (from older versions)
        var usersWithoutOrg = await dbContext.UserAccounts
            .Where(u => u.OrganizationId == Guid.Empty)
            .ToListAsync(cancellationToken);
        
        if (usersWithoutOrg.Count > 0)
        {
            foreach (var user in usersWithoutOrg)
            {
                user.OrganizationId = org.Id;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // 4. Seed Sample Connectors if none exist
        if (!await dbContext.Connectors.AnyAsync(cancellationToken))
        {
            var sqlSource = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "SQL Source - Sample",
                Type = ConnectorType.SqlServer,
                SqlHost = "localhost",
                SqlPort = 1433,
                SqlDatabase = "sampledb",
                SqlUsername = "sa",
                SqlPassword = sqlSecretProtector.Protect("ChangeMe!123"),
                SqlTrustServerCertificate = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var csvTarget = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "CSV Target - Sample",
                Type = ConnectorType.Csv,
                CsvPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "neoadapter", "exports", "sample-export.csv"),
                CsvDelimiter = ",",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var csvSource = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "CSV Source - Sample",
                Type = ConnectorType.Csv,
                CsvPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "neoadapter", "imports", "sample-import.csv"),
                CsvDelimiter = ",",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var sqlTarget = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "SQL Target - Sample",
                Type = ConnectorType.SqlServer,
                SqlHost = "localhost",
                SqlPort = 1433,
                SqlDatabase = "sampledb",
                SqlUsername = "sa",
                SqlPassword = sqlSecretProtector.Protect("ChangeMe!123"),
                SqlTrustServerCertificate = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            dbContext.Connectors.AddRange(sqlSource, csvTarget, csvSource, sqlTarget);

            dbContext.IntegrationJobs.AddRange(
                new IntegrationJob
                {
                    Id = Guid.NewGuid(),
                    Name = "SQL to CSV - Initial Job",
                    SourceConnectorId = sqlSource.Id,
                    DestinationConnectorId = csvTarget.Id,
                    OwnerOrganizationId = org.Id,
                    IsEnabled = true,
                    CronExpression = null,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                },
                new IntegrationJob
                {
                    Id = Guid.NewGuid(),
                    Name = "CSV to SQL - Initial Job",
                    SourceConnectorId = csvSource.Id,
                    DestinationConnectorId = sqlTarget.Id,
                    OwnerOrganizationId = org.Id,
                    IsEnabled = true,
                    CronExpression = null,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
