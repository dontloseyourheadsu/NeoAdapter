using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace NeoAdapter.IntegrationTests;

public class ApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithDatabase("neoadapter_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        // Note: Reference Program class from NeoAdapter.Api. 
        // Since it is top-level statements, it is defined in global namespace but the partial declaration 
        // in Program.cs maps it. Let's use the compiler's entry point Program.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresContainer.GetConnectionString());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (_client != null) _client.Dispose();
        if (_factory != null) await _factory.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    [Fact]
    public async Task GetRunsAndLogs_ShouldReturnExecutionHistoryAndLogs()
    {
        // 1. Register a test user
        var registerResponse = await _client!.PostAsJsonAsync("api/auth/register", new RegisterUserRequest("testuser", "Password123!"));
        registerResponse.EnsureSuccessStatusCode();
        var authInfo = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        authInfo.Should().NotBeNull();

        // Configure client to use the auth token
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authInfo!.AccessToken);

        // 2. Create organization context and seed data
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            
            var org = await db.Organizations.FirstAsync();
            var user = await db.UserAccounts.FirstAsync(u => u.Username == "testuser");
            
            user.OrganizationId = org.Id;
            db.UserAccounts.Update(user);

            var sourceConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "API Source PG",
                Type = ConnectorType.Postgres,
                SqlHost = "localhost",
                SqlPort = 5432,
                SqlDatabase = "neoadapter_test",
                SqlUsername = "postgres",
                SqlPassword = "postgres",
                SqlConfigJson = "{}"
            };

            var destConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "API Dest PG",
                Type = ConnectorType.Postgres,
                SqlHost = "localhost",
                SqlPort = 5432,
                SqlDatabase = "neoadapter_test",
                SqlUsername = "postgres",
                SqlPassword = "postgres",
                SqlConfigJson = "{}"
            };

            var job = new IntegrationJob
            {
                Id = Guid.NewGuid(),
                Name = "API Log Test Job",
                OwnerOrganizationId = org.Id,
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

            var run = new IntegrationJobRun
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = job.Id,
                Status = "SUCCEEDED",
                Message = "Transferred 10 rows.",
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                FinishedAtUtc = DateTimeOffset.UtcNow,
                RecordsProcessed = 10,
                StartedBy = "testuser"
            };

            var log1 = new IntegrationJobLogEntry
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = job.Id,
                IntegrationJobRunId = run.Id,
                LogLevel = "INFO",
                Message = "Connecting to Postgres database...",
                TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            };

            var log2 = new IntegrationJobLogEntry
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = job.Id,
                IntegrationJobRunId = run.Id,
                LogLevel = "INFO",
                Message = "Transfer finished successfully.",
                TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-4)
            };

            db.Connectors.AddRange(sourceConnector, destConnector);
            db.IntegrationJobs.Add(job);
            db.IntegrationJobRuns.Add(run);
            db.IntegrationJobLogs.AddRange(log1, log2);
            await db.SaveChangesAsync();

            // Act: Call api to get runs
            var runsResponse = await _client.GetAsync($"api/integration-jobs/{job.Id}/runs");
            runsResponse.EnsureSuccessStatusCode();
            var runs = await runsResponse.Content.ReadFromJsonAsync<IReadOnlyList<IntegrationJobRunDto>>();
            runs.Should().NotBeNull();
            runs!.Count.Should().Be(1);
            runs[0].StartedBy.Should().Be("testuser");
            runs[0].Status.Should().Be("SUCCEEDED");

            // Act: Call api to get logs
            var logsResponse = await _client.GetAsync($"api/integration-jobs/{job.Id}/logs?limit=10");
            logsResponse.EnsureSuccessStatusCode();
            var logsResult = await logsResponse.Content.ReadFromJsonAsync<JobLogsResponse>();
            logsResult.Should().NotBeNull();
            logsResult!.Logs.Count.Should().Be(2);
            logsResult.Logs[0].Message.Should().Be("Connecting to Postgres database...");
            logsResult.Logs[1].Message.Should().Be("Transfer finished successfully.");
            logsResult.NextCursor.Should().NotBeNull();
        }
    }
}
