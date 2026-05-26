using System;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Moq;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Application.Security;
using NeoAdapter.Domain;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NeoAdapter.IntegrationTests;

public class SqlServerConnectorTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServerContainer = new MsSqlBuilder()
        .Build();

    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithDatabase("test_pg_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly Mock<ISqlSecretProtector> _secretProtectorMock = new();

    public async Task InitializeAsync()
    {
        await _sqlServerContainer.StartAsync();
        await _postgresContainer.StartAsync();

        _secretProtectorMock.Setup(x => x.Unprotect(It.IsAny<string>())).Returns<string>(x => x);
    }

    public async Task DisposeAsync()
    {
        await _sqlServerContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferDataFromSqlServerToPostgres()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new NeoAdapterDbContext(options);

        // 1. Setup SQL Server source data
        await using var srcConn = new SqlConnection(_sqlServerContainer.GetConnectionString());
        await srcConn.OpenAsync();
        await using (var cmd = srcConn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE products (id INT PRIMARY KEY, title VARCHAR(100), price DECIMAL(18,2)); INSERT INTO products VALUES (1, 'Widget', 9.99), (2, 'Gadget', 19.99);";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Setup Postgres destination schema
        await using var destConn = new NpgsqlConnection(_postgresContainer.GetConnectionString());
        await destConn.OpenAsync();
        await using (var cmd = destConn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE products (id INT PRIMARY KEY, title VARCHAR(100), price NUMERIC);";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Setup Connectors
        var mssqlConnBuilder = new SqlConnectionStringBuilder(_sqlServerContainer.GetConnectionString());
        var sourceConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "SqlServer Source",
            Type = ConnectorType.SqlServer,
            SqlHost = mssqlConnBuilder.DataSource.Split(',')[0],
            SqlPort = int.Parse(mssqlConnBuilder.DataSource.Split(',')[1]),
            SqlDatabase = mssqlConnBuilder.InitialCatalog,
            SqlUsername = mssqlConnBuilder.UserID,
            SqlPassword = mssqlConnBuilder.Password,
            SqlTrustServerCertificate = true,
            SqlConfigJson = "{\"Tables\": [{\"Name\": \"products\", \"Fields\": [\"id\", \"title\", \"price\"]}]}"
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
            SqlConfigJson = "{}"
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "SqlServer to Postgres Job",
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
        checkCmd.CommandText = "SELECT COUNT(*) FROM products";
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        count.Should().Be(2);

        checkCmd.CommandText = "SELECT title FROM products WHERE id = 1";
        var title = (string?)await checkCmd.ExecuteScalarAsync();
        title.Should().Be("Widget");
    }
}
