using System.Data;
using System.Text.Json;
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

public class ConnectorIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _sourceContainer = new PostgreSqlBuilder()
        .WithDatabase("source_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly PostgreSqlContainer _destContainer = new PostgreSqlBuilder()
        .WithDatabase("dest_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly Mock<ISqlSecretProtector> _secretProtectorMock = new();

    public async Task InitializeAsync()
    {
        await _sourceContainer.StartAsync();
        await _destContainer.StartAsync();

        _secretProtectorMock.Setup(x => x.Unprotect(It.IsAny<string>())).Returns<string>(x => x);
    }

    public async Task DisposeAsync()
    {
        await _sourceContainer.DisposeAsync();
        await _destContainer.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferDataFromPostgresToPostgres()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new NeoAdapterDbContext(options);

        // 1. Setup source data
        await using var sourceConn = new NpgsqlConnection(_sourceContainer.GetConnectionString());
        await sourceConn.OpenAsync();
        await using (var cmd = sourceConn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(100)); INSERT INTO users VALUES (1, 'Alice'), (2, 'Bob');";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Setup destination schema
        await using var destConn = new NpgsqlConnection(_destContainer.GetConnectionString());
        await destConn.OpenAsync();
        await using (var cmd = destConn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(100));";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Create Connectors and Job
        var sourceConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Source",
            Type = ConnectorType.Postgres,
            SqlHost = _sourceContainer.Hostname,
            SqlPort = _sourceContainer.GetMappedPublicPort(5432),
            SqlDatabase = "source_db",
            SqlUsername = "postgres",
            SqlPassword = "postgres",
            SqlConfigJson = "{\"Tables\": [{\"Name\": \"users\", \"Fields\": [\"id\", \"name\"]}]}"
        };

        var destConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Destination",
            Type = ConnectorType.Postgres,
            SqlHost = _destContainer.Hostname,
            SqlPort = _destContainer.GetMappedPublicPort(5432),
            SqlDatabase = "dest_db",
            SqlUsername = "postgres",
            SqlPassword = "postgres",
            SqlConfigJson = "{}" // Not used for destination in this flow currently
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "Test Job",
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
        await using var checkCmd = destConn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM Users";
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        count.Should().Be(2);

        checkCmd.CommandText = "SELECT Name FROM Users WHERE Id = 1";
        var name = (string?)await checkCmd.ExecuteScalarAsync();
        name.Should().Be("Alice");
    }
}
