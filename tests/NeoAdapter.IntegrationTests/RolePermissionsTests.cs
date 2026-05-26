using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Domain;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NeoAdapter.IntegrationTests;

public class DbFixture : IAsyncLifetime
{
    public PostgreSqlContainer PostgresContainer { get; } = new PostgreSqlBuilder()
        .WithDatabase("neoadapter_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await PostgresContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await PostgresContainer.DisposeAsync();
    }
}

public class RolePermissionsTests : IClassFixture<DbFixture>, IAsyncLifetime
{
    private readonly DbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public RolePermissionsTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _fixture.PostgresContainer.GetConnectionString());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client != null) _client.Dispose();
        if (_factory != null) await _factory.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
    }

    private async Task<string> AuthenticateUserAsync(string username, Action<UserAccount> configureUser)
    {
        // 1. Register user via API
        var registerResponse = await _client!.PostAsJsonAsync("api/auth/register", new RegisterUserRequest(username, "Password123!"));
        registerResponse.EnsureSuccessStatusCode();
        var authInfo = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        authInfo.Should().NotBeNull();

        // 2. Configure their role in the DB
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var dbUser = await db.UserAccounts.FirstAsync(u => u.Username == username);
            configureUser(dbUser);
            db.UserAccounts.Update(dbUser);
            await db.SaveChangesAsync();
        }

        return authInfo!.AccessToken;
    }

    [Fact]
    public async Task ReadOnlyUser_Should_BeBlockedFromCreatingOrRunning()
    {
        // Create user with Read only
        var token = await AuthenticateUserAsync("readuser", u =>
        {
            u.RoleRead = true;
            u.RoleEdit = false;
            u.RoleCreate = false;
            u.RoleAdmin = false;
        });

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Try creating connector
        var createConnectorReq = new CreateConnectorRequest(
            Name: "ReadUser Connector",
            Type: NeoAdapter.Contracts.Connectors.ConnectorType.Csv,
            Sql: null,
            Csv: new CsvConnectorSettingsDto("/tmp/sample.csv", ","),
            Excel: null
        );
        var connResponse = await client.PostAsJsonAsync("api/connectors", createConnectorReq);
        connResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Try creating job
        var createJobReq = new CreateIntegrationJobRequest(
            Name: "ReadUser Job",
            Steps: new List<CreateIntegrationJobStepRequest>(),
            IsEnabled: true,
            CronExpression: null
        );
        var jobResponse = await client.PostAsJsonAsync("api/integration-jobs", createJobReq);
        jobResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EditUser_CanRunJob_ButCannotCreateJobOrConnector()
    {
        Guid orgId;
        Guid jobId;

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var org = await db.Organizations.FirstAsync();
            orgId = org.Id;

            // Pre-create a job in DB
            var sourceConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "PG Source - EditUser",
                Type = NeoAdapter.Domain.ConnectorType.Postgres,
                SqlConfigJson = "{}",
                OwnerOrganizationId = orgId
            };
            var destConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "PG Dest - EditUser",
                Type = NeoAdapter.Domain.ConnectorType.Postgres,
                SqlConfigJson = "{}",
                OwnerOrganizationId = orgId
            };
            var job = new IntegrationJob
            {
                Id = Guid.NewGuid(),
                Name = "Test Job Edit Role - EditUser",
                OwnerOrganizationId = orgId,
                IsEnabled = true
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

            db.Connectors.AddRange(sourceConnector, destConnector);
            db.IntegrationJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        // Create edit user in same organization
        var token = await AuthenticateUserAsync("edituser", u =>
        {
            u.OrganizationId = orgId;
            u.RoleRead = true;
            u.RoleEdit = true;
            u.RoleCreate = false;
            u.RoleAdmin = false;
        });

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Try creating connector -> Forbidden
        var createConnectorReq = new CreateConnectorRequest(
            Name: "EditUser Connector",
            Type: NeoAdapter.Contracts.Connectors.ConnectorType.Csv,
            Sql: null,
            Csv: new CsvConnectorSettingsDto("/tmp/sample.csv", ","),
            Excel: null
        );
        var connResponse = await client.PostAsJsonAsync("api/connectors", createConnectorReq);
        connResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Try running job -> Accepted/Ok (Edit role allows running!)
        var runResponse = await client.PostAsync($"api/integration-jobs/{jobId}/run", null);
        runResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task CreateUser_CanCreateJobAndConnector()
    {
        Guid orgId;

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var org = await db.Organizations.FirstAsync();
            orgId = org.Id;
        }

        var token = await AuthenticateUserAsync("createuser", u =>
        {
            u.OrganizationId = orgId;
            u.RoleRead = true;
            u.RoleEdit = true;
            u.RoleCreate = true;
            u.RoleAdmin = false;
        });

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create connector -> Success
        var createConnectorReq = new CreateConnectorRequest(
            Name: "CreateUser Connector",
            Type: NeoAdapter.Contracts.Connectors.ConnectorType.Csv,
            Sql: null,
            Csv: new CsvConnectorSettingsDto("/tmp/sample.csv", ","),
            Excel: null
        );
        var connResponse = await client.PostAsJsonAsync("api/connectors", createConnectorReq);
        connResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create job -> Success
        var step = new CreateIntegrationJobStepRequest(
            OrderIndex: 0,
            SourceType: NeoAdapter.Contracts.Connectors.ConnectorType.Csv,
            SourceSql: null,
            SourceCsv: new CsvConnectorSettingsDto("/tmp/in.csv", ","),
            DestinationType: NeoAdapter.Contracts.Connectors.ConnectorType.Csv,
            DestinationSql: null,
            DestinationCsv: new CsvConnectorSettingsDto("/tmp/out.csv", ",")
        );
        var createJobReq = new CreateIntegrationJobRequest(
            Name: "CreateUser Job",
            Steps: new List<CreateIntegrationJobStepRequest> { step },
            IsEnabled: true,
            CronExpression: null,
            OwnerOrganizationId: orgId
        );
        var jobResponse = await client.PostAsJsonAsync("api/integration-jobs", createJobReq);
        jobResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminUser_BypassesGroupChecks_InSameOrganization()
    {
        Guid orgId;
        Guid otherGroupId;
        Guid otherJobId;

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var org = await db.Organizations.FirstAsync();
            orgId = org.Id;

            // Create another group
            var group = new Group
            {
                Id = Guid.NewGuid(),
                Name = "Other Group - AdminUser",
                OrganizationId = orgId,
                CreatorUserId = Guid.NewGuid()
            };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            otherGroupId = group.Id;

            // Create job owned by other group
            var sourceConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "Group PG Source - AdminUser",
                Type = NeoAdapter.Domain.ConnectorType.Postgres,
                SqlConfigJson = "{}",
                OwnerOrganizationId = orgId,
                OwnerGroupId = otherGroupId
            };
            var destConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "Group PG Dest - AdminUser",
                Type = NeoAdapter.Domain.ConnectorType.Postgres,
                SqlConfigJson = "{}",
                OwnerOrganizationId = orgId,
                OwnerGroupId = otherGroupId
            };
            var job = new IntegrationJob
            {
                Id = Guid.NewGuid(),
                Name = "Group Owned Job - AdminUser",
                OwnerOrganizationId = orgId,
                OwnerGroupId = otherGroupId,
                IsEnabled = true
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

            db.Connectors.AddRange(sourceConnector, destConnector);
            db.IntegrationJobs.Add(job);
            await db.SaveChangesAsync();
            otherJobId = job.Id;
        }

        // Create Admin user (not in the other group)
        var token = await AuthenticateUserAsync("adminuser", u =>
        {
            u.OrganizationId = orgId;
            u.GroupId = null; // No group
            u.Role = "Admin";
            u.RoleRead = true;
            u.RoleEdit = true;
            u.RoleCreate = true;
            u.RoleAdmin = true;
        });

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Get jobs -> Admin can see the group-owned job
        var getJobsResponse = await client.GetAsync("api/integration-jobs");
        getJobsResponse.EnsureSuccessStatusCode();
        var jobs = await getJobsResponse.Content.ReadFromJsonAsync<IReadOnlyList<IntegrationJobDto>>();
        jobs.Should().NotBeNull();
        jobs!.Any(j => j.Id == otherJobId).Should().BeTrue();

        // Run job -> Admin can run it
        var runResponse = await client.PostAsync($"api/integration-jobs/{otherJobId}/run", null);
        runResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task DemoteLastAdmin_Should_ReturnBadRequest()
    {
        Guid orgId;
        Guid adminUserId;

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var org = await db.Organizations.FirstAsync();
            orgId = org.Id;

            // Make sure there are no other admins
            var admins = await db.UserAccounts.Where(u => u.OrganizationId == orgId && u.RoleAdmin).ToListAsync();
            foreach (var a in admins)
            {
                a.RoleAdmin = false;
                a.Role = "User";
                db.UserAccounts.Update(a);
            }
            await db.SaveChangesAsync();
        }

        // Authenticate admin (will be the ONLY admin)
        var token = await AuthenticateUserAsync("onlyadmin", u =>
        {
            u.OrganizationId = orgId;
            u.Role = "Admin";
            u.RoleRead = true;
            u.RoleEdit = true;
            u.RoleCreate = true;
            u.RoleAdmin = true;
        });

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            adminUserId = (await db.UserAccounts.FirstAsync(u => u.Username == "onlyadmin")).Id;
        }

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Try to demote self (the last admin)
        var updateRequest = new UpdateUserRolesRequest(
            RoleRead: true,
            RoleEdit: true,
            RoleCreate: true,
            RoleAdmin: false,
            GroupId: null
        );
        var demoteResponse = await client.PutAsJsonAsync($"api/org-admin/users/{adminUserId}/roles", updateRequest);
        demoteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CrossOrganizationAccess_Should_BeBlocked()
    {
        Guid firstOrgId;
        Guid secondOrgId;
        Guid secondOrgJobId;

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var firstOrg = await db.Organizations.FirstAsync();
            firstOrgId = firstOrg.Id;

            // Create second organization
            var secondOrg = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Second Org - CrossOrg",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Organizations.Add(secondOrg);
            await db.SaveChangesAsync();
            secondOrgId = secondOrg.Id;

            // Create connector & job in second organization
            var sourceConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "Second Org PG Source - CrossOrg",
                Type = NeoAdapter.Domain.ConnectorType.Postgres,
                SqlConfigJson = "{}",
                OwnerOrganizationId = secondOrgId
            };
            var destConnector = new Connector
            {
                Id = Guid.NewGuid(),
                Name = "Second Org PG Dest - CrossOrg",
                Type = NeoAdapter.Domain.ConnectorType.Postgres,
                SqlConfigJson = "{}",
                OwnerOrganizationId = secondOrgId
            };
            var job = new IntegrationJob
            {
                Id = Guid.NewGuid(),
                Name = "Second Org Job - CrossOrg",
                OwnerOrganizationId = secondOrgId,
                IsEnabled = true
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

            db.Connectors.AddRange(sourceConnector, destConnector);
            db.IntegrationJobs.Add(job);
            await db.SaveChangesAsync();
            secondOrgJobId = job.Id;
        }

        // Authenticate admin in FIRST organization
        var token = await AuthenticateUserAsync("firstorgadmin", u =>
        {
            u.OrganizationId = firstOrgId;
            u.Role = "Admin";
            u.RoleRead = true;
            u.RoleEdit = true;
            u.RoleCreate = true;
            u.RoleAdmin = true;
        });

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Try to access second organization's job -> Should return NotFound or Forbidden
        var runResponse = await client.PostAsync($"api/integration-jobs/{secondOrgJobId}/run", null);
        runResponse.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }
}
