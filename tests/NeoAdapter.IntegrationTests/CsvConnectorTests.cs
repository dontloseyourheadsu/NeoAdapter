using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Application.Security;
using NeoAdapter.Domain;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NeoAdapter.IntegrationTests;

public class CsvConnectorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithDatabase("test_pg_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly Mock<ISqlSecretProtector> _secretProtectorMock = new();
    private readonly List<string> _tempFiles = new();

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _secretProtectorMock.Setup(x => x.Unprotect(It.IsAny<string>())).Returns<string>(x => x);
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch {}
        }
    }

    private string GetTempCsvPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferDataFromPostgresToCsv()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new NeoAdapterDbContext(options);

        // 1. Setup Postgres source data
        await using var conn = new NpgsqlConnection(_postgresContainer.GetConnectionString());
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(100)); INSERT INTO users VALUES (1, 'Alice'), (2, 'Bob');";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Setup Connectors
        var csvPath = GetTempCsvPath();
        var sourceConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Postgres Source",
            Type = ConnectorType.Postgres,
            SqlHost = _postgresContainer.Hostname,
            SqlPort = _postgresContainer.GetMappedPublicPort(5432),
            SqlDatabase = "test_pg_db",
            SqlUsername = "postgres",
            SqlPassword = "postgres",
            SqlConfigJson = "{\"Tables\": [{\"Name\": \"users\", \"Fields\": [\"id\", \"name\"]}]}"
        };

        var destConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "CSV Destination",
            Type = ConnectorType.Csv,
            CsvPath = csvPath,
            CsvDelimiter = ","
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "Postgres to CSV Job",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        job.Steps.Add(new IntegrationJobStep
        {
            Id = Guid.NewGuid(),
            IntegrationJobId = job.Id,
            OrderIndex = 0,
            SourceConnectorId = sourceConnector.Id,
            DestinationConnectorId = destConnector.Id,
            SourceConnector = sourceConnector,
            DestinationConnector = destConnector
        });

        dbContext.Connectors.AddRange(sourceConnector, destConnector);
        dbContext.IntegrationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var executor = new IntegrationJobExecutor(dbContext, _secretProtectorMock.Object);

        // Act
        await executor.ExecuteAsync(job.Id);

        // Assert
        File.Exists(csvPath).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(csvPath);
        lines.Length.Should().Be(3); // Header + 2 rows
        lines[0].Should().Be("id,name");
        lines[1].Should().Be("1,Alice");
        lines[2].Should().Be("2,Bob");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferDataFromCsvToPostgres()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new NeoAdapterDbContext(options);

        // 1. Setup CSV file
        var csvPath = GetTempCsvPath();
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "id,name",
            "10,Charlie",
            "20,Diana"
        });

        // 2. Setup Postgres destination schema
        await using var conn = new NpgsqlConnection(_postgresContainer.GetConnectionString());
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(100));";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Setup Connectors
        var sourceConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "CSV Source",
            Type = ConnectorType.Csv,
            CsvPath = csvPath,
            CsvDelimiter = ","
        };

        var destConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Postgres Destination",
            Type = ConnectorType.Postgres,
            SqlHost = _postgresContainer.Hostname,
            SqlPort = _postgresContainer.GetMappedPublicPort(5432),
            SqlDatabase = "test_pg_db",
            SqlUsername = "postgres",
            SqlPassword = "postgres",
            SqlConfigJson = "{\"Tables\": [{\"Name\": \"users\", \"Fields\": [\"id\", \"name\"]}]}"
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "CSV to Postgres Job",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        job.Steps.Add(new IntegrationJobStep
        {
            Id = Guid.NewGuid(),
            IntegrationJobId = job.Id,
            OrderIndex = 0,
            SourceConnectorId = sourceConnector.Id,
            DestinationConnectorId = destConnector.Id,
            SourceConnector = sourceConnector,
            DestinationConnector = destConnector
        });

        dbContext.Connectors.AddRange(sourceConnector, destConnector);
        dbContext.IntegrationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var executor = new IntegrationJobExecutor(dbContext, _secretProtectorMock.Object);

        // Act
        await executor.ExecuteAsync(job.Id);

        // Assert
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM users";
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        count.Should().Be(2);

        checkCmd.CommandText = "SELECT name FROM users WHERE id = 10";
        var name = (string?)await checkCmd.ExecuteScalarAsync();
        name.Should().Be("Charlie");
    }
}
