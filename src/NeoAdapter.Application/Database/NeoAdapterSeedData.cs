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
        var org = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Name == "NeoAdapter", cancellationToken);
        if (org == null)
        {
            org = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "NeoAdapter",
                CreatedAtUtc = now
            };
            dbContext.Organizations.Add(org);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // 2. Ensure Admin User exists
        var admin = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Username.ToLower() == "admin", cancellationToken);
        if (admin == null)
        {
            var salt = Guid.NewGuid().ToString();
            admin = new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                PasswordSalt = salt,
                PasswordHash = passwordHasher.HashPassword("Admin123!").Hash,
                OrganizationId = org.Id,
                Role = "Admin",
                RoleRead = true,
                RoleEdit = true,
                RoleCreate = true,
                RoleAdmin = true,
                CreatedAtUtc = now
            };
            dbContext.UserAccounts.Add(admin);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // 3. Seed Sample Connectors and Jobs if none exist
        if (!await dbContext.IntegrationJobs.AnyAsync(cancellationToken))
        {
            var sqlConfig = "{\"Tables\": [{\"Name\": \"Products\", \"Fields\": [\"Id\", \"Name\", \"Price\", \"Stock\"]}]}";

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
                SqlConfigJson = sqlConfig,
                OwnerOrganizationId = org.Id,
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
                OwnerOrganizationId = org.Id,
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
                OwnerOrganizationId = org.Id,
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
                SqlConfigJson = sqlConfig,
                OwnerOrganizationId = org.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            dbContext.Connectors.AddRange(sqlSource, csvTarget, csvSource, sqlTarget);

            var sqlToCsvJob = new IntegrationJob
            {
                Id = Guid.NewGuid(),
                Name = "SQL to CSV - Initial Job",
                OwnerOrganizationId = org.Id,
                IsEnabled = true,
                CronExpression = null,
                CreatorUserId = admin.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            sqlToCsvJob.Owners.Add(admin);

            var csvToSqlJob = new IntegrationJob
            {
                Id = Guid.NewGuid(),
                Name = "CSV to SQL - Initial Job",
                OwnerOrganizationId = org.Id,
                IsEnabled = true,
                CronExpression = null,
                CreatorUserId = admin.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            csvToSqlJob.Owners.Add(admin);

            dbContext.IntegrationJobs.AddRange(sqlToCsvJob, csvToSqlJob);

            dbContext.Set<IntegrationJobStep>().AddRange(
                new IntegrationJobStep
                {
                    Id = Guid.NewGuid(),
                    IntegrationJobId = sqlToCsvJob.Id,
                    OrderIndex = 0,
                    SourceConnectorId = sqlSource.Id,
                    DestinationConnectorId = csvTarget.Id
                },
                new IntegrationJobStep
                {
                    Id = Guid.NewGuid(),
                    IntegrationJobId = csvToSqlJob.Id,
                    OrderIndex = 0,
                    SourceConnectorId = csvSource.Id,
                    DestinationConnectorId = sqlTarget.Id
                });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
