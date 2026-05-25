using System.Data;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;
using Moq;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Application.Security;
using NeoAdapter.Domain;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NeoAdapter.IntegrationTests;

public class ExcelConnectorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithDatabase("test_db")
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
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private string GetTempExcelPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferDataFromPostgresToExcel()
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
            cmd.CommandText = @"
                CREATE TABLE employees (id INT PRIMARY KEY, name VARCHAR(100), salary NUMERIC); 
                INSERT INTO employees VALUES (1, 'Alice', 60000), (2, 'Bob', 80000);";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Setup Connectors
        var excelPath = GetTempExcelPath();
        var sourceConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Postgres Source",
            Type = ConnectorType.Postgres,
            SqlHost = _postgresContainer.Hostname,
            SqlPort = _postgresContainer.GetMappedPublicPort(5432),
            SqlDatabase = "test_db",
            SqlUsername = "postgres",
            SqlPassword = "postgres",
            SqlConfigJson = "{\"Tables\": [{\"Name\": \"employees\", \"Fields\": [\"id\", \"name\", \"salary\"]}]}"
        };

        var destConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Excel Destination",
            Type = ConnectorType.Excel,
            ExcelPath = excelPath,
            ExcelSheetName = "EmployeesList"
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "Postgres to Excel Job",
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
        File.Exists(excelPath).Should().BeTrue();

        var rows = MiniExcel.Query(excelPath, sheetName: "EmployeesList").Cast<IDictionary<string, object>>().ToList();
        rows.Count.Should().Be(2);

        // Row 1 Assertions
        var firstRow = rows[0];
        firstRow["id"].ToString().Should().Be("1");
        firstRow["name"].ToString().Should().Be("Alice");
        firstRow["salary"].ToString().Should().Be("60000");

        // Row 2 Assertions
        var secondRow = rows[1];
        secondRow["id"].ToString().Should().Be("2");
        secondRow["name"].ToString().Should().Be("Bob");
        secondRow["salary"].ToString().Should().Be("80000");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferDataFromExcelToPostgres()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new NeoAdapterDbContext(options);

        // 1. Create source Excel file
        var excelPath = GetTempExcelPath();
        var excelData = new[]
        {
            new { Id = 10, Name = "Charlie", Department = "HR" },
            new { Id = 20, Name = "Diana", Department = "IT" }
        };
        await MiniExcel.SaveAsAsync(excelPath, excelData, sheetName: "Staff");

        // 2. Setup Postgres destination table schema
        await using var conn = new NpgsqlConnection(_postgresContainer.GetConnectionString());
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE staff (id INT PRIMARY KEY, name VARCHAR(100), department VARCHAR(50));";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Setup Connectors
        var sourceConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Excel Source",
            Type = ConnectorType.Excel,
            ExcelPath = excelPath,
            ExcelSheetName = "Staff"
        };

        var destConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Postgres Destination",
            Type = ConnectorType.Postgres,
            SqlHost = _postgresContainer.Hostname,
            SqlPort = _postgresContainer.GetMappedPublicPort(5432),
            SqlDatabase = "test_db",
            SqlUsername = "postgres",
            SqlPassword = "postgres",
            SqlConfigJson = "{\"Tables\": [{\"Name\": \"staff\", \"Fields\": [\"id\", \"name\", \"department\"]}]}"
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "Excel to Postgres Job",
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
        checkCmd.CommandText = "SELECT COUNT(*) FROM staff";
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        count.Should().Be(2);

        checkCmd.CommandText = "SELECT name FROM staff WHERE id = 10";
        var name10 = (string?)await checkCmd.ExecuteScalarAsync();
        name10.Should().Be("Charlie");

        checkCmd.CommandText = "SELECT department FROM staff WHERE id = 20";
        var dept20 = (string?)await checkCmd.ExecuteScalarAsync();
        dept20.Should().Be("IT");
    }
}
