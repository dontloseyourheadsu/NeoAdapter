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
    .AddProject("frontend-web", "../NeoAdapter.Frontend.WebBlazor/NeoAdapter.Frontend.WebBlazor.csproj")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
