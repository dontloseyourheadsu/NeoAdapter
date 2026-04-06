var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres-server")
    .WithDataVolume("neoadapter-postgres-data");

var neoAdapterDatabase = postgres.AddDatabase("Postgres", "neoadapter_dev");

var api = builder
    .AddProject("api", "../NeoAdapter.Api/NeoAdapter.Api.csproj")
    .WithReference(neoAdapterDatabase)
    .WaitFor(neoAdapterDatabase);

builder
    .AddProject("frontend-browser", "../NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj")
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("NEOADAPTER_API_BASE_URL", api.GetEndpoint("http"));

builder.Build().Run();
