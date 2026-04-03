using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database;

public static class NeoAdapterSeedData
{
    public static async Task SeedAsync(NeoAdapterDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await dbContext.Connectors.AnyAsync(cancellationToken)
            || await dbContext.IntegrationJobs.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var sqlSource = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "SQL Source - Sample",
            Type = ConnectorType.Sql,
            SqlServer = "localhost",
            SqlPort = 1433,
            SqlDatabase = "sampledb",
            SqlUsername = "sa",
            SqlPassword = "ChangeMe!123",
            SqlTable = "dbo.SourceTable",
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
            Type = ConnectorType.Sql,
            SqlServer = "localhost",
            SqlPort = 1433,
            SqlDatabase = "sampledb",
            SqlUsername = "sa",
            SqlPassword = "ChangeMe!123",
            SqlTable = "dbo.TargetTable",
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
                IsEnabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new IntegrationJob
            {
                Id = Guid.NewGuid(),
                Name = "CSV to SQL - Initial Job",
                SourceConnectorId = csvSource.Id,
                DestinationConnectorId = sqlTarget.Id,
                IsEnabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}