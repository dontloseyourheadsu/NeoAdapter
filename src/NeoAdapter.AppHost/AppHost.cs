var builder = DistributedApplication.CreateBuilder(args);

// Postgres server
var postgres = builder
    .AddPostgres("postgres-server", port: 5432)
    .WithDataVolume("neoadapter-postgres-data");

var neoAdapterDatabase = postgres.AddDatabase("Postgres", "neoadapter_dev");
var testPostgresDb = postgres.AddDatabase("PostgresTest", "testdb");

// SQL Server
var sqlPassword = builder.AddParameter("sqlserver-password", secret: true);
var sqlServer = builder
    .AddSqlServer("sqlserver-server", sqlPassword, port: 1433)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithDataVolume("neoadapter-sqlserver-data");

var sqlServerDatabase = sqlServer.AddDatabase("SqlServer", "neoadapter_dev");
var testSqlServerDb = sqlServer.AddDatabase("SqlServerTest", "testdb");

// DB Initializer
var dbInitializer = builder
    .AddProject("db-initializer", "../NeoAdapter.DbInitializer/NeoAdapter.DbInitializer.csproj")
    .WithReference(neoAdapterDatabase)
    .WithReference(sqlServerDatabase)
    .WithReference(testPostgresDb)
    .WithReference(testSqlServerDb)
    .WaitFor(neoAdapterDatabase)
    .WaitFor(sqlServerDatabase)
    .WaitFor(testPostgresDb)
    .WaitFor(testSqlServerDb);

var api = builder
    .AddProject("api", "../NeoAdapter.Api/NeoAdapter.Api.csproj")
    .WithReference(neoAdapterDatabase)
    .WithReference(sqlServerDatabase)
    .WithReference(testPostgresDb)
    .WithReference(testSqlServerDb)
    .WaitFor(neoAdapterDatabase)
    .WaitFor(sqlServerDatabase)
    .WaitFor(testPostgresDb)
    .WaitFor(testSqlServerDb)
    .WaitFor(dbInitializer);

builder
    .AddProject("frontend-web", "../NeoAdapter.Frontend.WebBlazor/NeoAdapter.Frontend.WebBlazor.csproj")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
