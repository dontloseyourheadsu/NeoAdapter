using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Moq;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Application.SqlEditor;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.SqlEditor;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;
using ConnectorType = NeoAdapter.Contracts.Connectors.ConnectorType;

namespace NeoAdapter.IntegrationTests;

public class SqlEditorIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServerContainer = new MsSqlBuilder().Build();

    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithDatabase("test_pg_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly Mock<IConnectorService> _connectorServiceMock = new();
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
    public async Task SqlServer_SchemaAndExecution_ShouldWorkCorrectly()
    {
        // Arrange
        // 1. Setup Sql Server objects
        await using var conn = new SqlConnection(_sqlServerContainer.GetConnectionString());
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE editor_table (id INT PRIMARY KEY, name VARCHAR(100));
                INSERT INTO editor_table VALUES (1, 'Alice'), (2, 'Bob');
            ";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE VIEW editor_view AS SELECT id, name FROM editor_table;";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE PROCEDURE editor_proc AS BEGIN SELECT name FROM editor_table; END;";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE FUNCTION editor_func() RETURNS INT AS BEGIN RETURN 100; END;";
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new NeoAdapterDbContext(options);

        var service = new SqlEditorService(dbContext, _connectorServiceMock.Object, _secretProtectorMock.Object);

        var connBuilder = new SqlConnectionStringBuilder(_sqlServerContainer.GetConnectionString());
        var connectionSettings = new SqlEditorConnectionSettings(
            Host: connBuilder.DataSource.Split(',')[0],
            Port: int.Parse(connBuilder.DataSource.Split(',')[1]),
            Database: connBuilder.InitialCatalog,
            Username: connBuilder.UserID,
            Password: connBuilder.Password,
            TrustServerCertificate: true
        );

        // Act & Assert 1: Get Schema
        var schemaReq = new GetSchemaRequest(
            ConnectorId: null,
            ConnectionString: null,
            ConnectionSettings: connectionSettings,
            Type: ConnectorType.SqlServer,
            SaveConnection: false,
            ConnectionName: null
        );

        var schemaResult = await service.GetSchemaAsync(schemaReq, Guid.NewGuid(), Guid.NewGuid(), null, "User", true, true, false, default);

        schemaResult.Tables.Should().Contain(t => t.Name.Equals("editor_table", StringComparison.OrdinalIgnoreCase));
        schemaResult.Views.Should().Contain(v => v.Name.Equals("editor_view", StringComparison.OrdinalIgnoreCase));
        schemaResult.Procedures.Should().Contain(p => p.Name.Equals("editor_proc", StringComparison.OrdinalIgnoreCase));
        schemaResult.Functions.Should().Contain(f => f.Name.Equals("editor_func", StringComparison.OrdinalIgnoreCase));

        // Act & Assert 2: Execute Valid Query
        var queryReq = new ExecuteQueryRequest(
            ConnectorId: null,
            ConnectionString: null,
            ConnectionSettings: connectionSettings,
            Type: ConnectorType.SqlServer,
            Query: "SELECT id, name FROM editor_table ORDER BY id ASC",
            SaveConnection: false,
            ConnectionName: null
        );

        var queryResult = await service.ExecuteQueryAsync(queryReq, Guid.NewGuid(), Guid.NewGuid(), null, "User", true, true, false, default);

        queryResult.ErrorMessage.Should().BeNull();
        queryResult.Columns.Should().Equal("id", "name");
        queryResult.Rows.Should().HaveCount(2);
        queryResult.Rows[0][0].Should().Be(1);
        queryResult.Rows[0][1].Should().Be("Alice");

        // Act & Assert 3: Explain Query
        var explainReq = queryReq with { ExplainOnly = true };
        var explainResult = await service.ExecuteQueryAsync(explainReq, Guid.NewGuid(), Guid.NewGuid(), null, "User", true, true, false, default);

        explainResult.ErrorMessage.Should().BeNull();
        explainResult.ExplainPlan.Should().NotBeNullOrEmpty();
        explainResult.ExplainPlan.Should().Contain("editor_table");

        // Act & Assert 4: Block Invalid Query
        var invalidReq = queryReq with { Query = "DELETE FROM editor_table" };
        var invalidResult = await service.ExecuteQueryAsync(invalidReq, Guid.NewGuid(), Guid.NewGuid(), null, "User", true, true, false, default);

        invalidResult.ErrorMessage.Should().Contain("blocked");
        invalidResult.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Postgres_SchemaAndExecution_ShouldWorkCorrectly()
    {
        // Arrange
        // 1. Setup Postgres objects
        await using var conn = new NpgsqlConnection(_postgresContainer.GetConnectionString());
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE editor_table (id INT PRIMARY KEY, name VARCHAR(100));
                INSERT INTO editor_table VALUES (1, 'Charlie'), (2, 'Delta');
            ";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE VIEW editor_view AS SELECT id, name FROM editor_table;";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE PROCEDURE editor_proc() LANGUAGE plpgsql AS $$ BEGIN END; $$;";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE FUNCTION editor_func() RETURNS INT LANGUAGE plpgsql AS $$ BEGIN RETURN 200; END; $$;";
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new NeoAdapterDbContext(options);

        var service = new SqlEditorService(dbContext, _connectorServiceMock.Object, _secretProtectorMock.Object);

        var connectionSettings = new SqlEditorConnectionSettings(
            Host: _postgresContainer.Hostname,
            Port: _postgresContainer.GetMappedPublicPort(5432),
            Database: "test_pg_db",
            Username: "postgres",
            Password: "postgres",
            TrustServerCertificate: true
        );

        // Act & Assert 1: Get Schema
        var schemaReq = new GetSchemaRequest(
            ConnectorId: null,
            ConnectionString: null,
            ConnectionSettings: connectionSettings,
            Type: ConnectorType.Postgres,
            SaveConnection: false,
            ConnectionName: null
        );

        var schemaResult = await service.GetSchemaAsync(schemaReq, Guid.NewGuid(), Guid.NewGuid(), null, "User", true, true, false, default);

        schemaResult.Tables.Should().Contain(t => t.Name.Equals("editor_table", StringComparison.OrdinalIgnoreCase));
        schemaResult.Views.Should().Contain(v => v.Name.Equals("editor_view", StringComparison.OrdinalIgnoreCase));
        schemaResult.Procedures.Should().Contain(p => p.Name.Equals("editor_proc", StringComparison.OrdinalIgnoreCase));
        schemaResult.Functions.Should().Contain(f => f.Name.Equals("editor_func", StringComparison.OrdinalIgnoreCase));

        // Act & Assert 2: Execute Valid Query
        var queryReq = new ExecuteQueryRequest(
            ConnectorId: null,
            ConnectionString: null,
            ConnectionSettings: connectionSettings,
            Type: ConnectorType.Postgres,
            Query: "SELECT id, name FROM editor_table ORDER BY id ASC",
            SaveConnection: false,
            ConnectionName: null
        );

        var queryResult = await service.ExecuteQueryAsync(queryReq, Guid.NewGuid(), Guid.NewGuid(), null, "User", true, true, false, default);

        queryResult.ErrorMessage.Should().BeNull();
        queryResult.Columns.Should().Equal("id", "name");
        queryResult.Rows.Should().HaveCount(2);
        queryResult.Rows[0][0].Should().Be(1);
        queryResult.Rows[0][1].Should().Be("Charlie");

        // Act & Assert 3: Block Invalid Query
        var invalidReq = queryReq with { Query = "DROP TABLE editor_table" };
        var invalidResult = await service.ExecuteQueryAsync(invalidReq, Guid.NewGuid(), Guid.NewGuid(), null, "User", true, true, false, default);

        invalidResult.ErrorMessage.Should().Contain("blocked");
        invalidResult.Rows.Should().BeEmpty();
    }
}
