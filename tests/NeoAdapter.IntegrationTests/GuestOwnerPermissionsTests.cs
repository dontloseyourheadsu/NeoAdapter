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
using Microsoft.Extensions.DependencyInjection;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Domain;
using Xunit;

namespace NeoAdapter.IntegrationTests;

public class GuestOwnerPermissionsTests : IClassFixture<DbFixture>, IAsyncLifetime
{
    private readonly DbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public GuestOwnerPermissionsTests(DbFixture fixture)
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

    private async Task<(string Token, Guid UserId, Guid OrgId)> AuthenticateUserAsync(string username, Action<UserAccount>? configureUser = null)
    {
        var registerResponse = await _client!.PostAsJsonAsync("api/auth/register", new RegisterUserRequest(username, "Password123!"));
        registerResponse.EnsureSuccessStatusCode();
        var authInfo = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        authInfo.Should().NotBeNull();

        Guid userId;
        Guid orgId;

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var dbUser = await db.UserAccounts.FirstAsync(u => u.Username == username);
            if (configureUser != null)
            {
                configureUser(dbUser);
                db.UserAccounts.Update(dbUser);
                await db.SaveChangesAsync();
            }
            userId = dbUser.Id;
            orgId = dbUser.OrganizationId;
        }

        return (authInfo!.AccessToken, userId, orgId);
    }

    [Fact]
    public async Task NewJob_HasCreatorAsOwner_AndCreatorCannotBeRemoved()
    {
        // 1. Create creator user
        var creator = await AuthenticateUserAsync("job_creator", u =>
        {
            u.RoleCreate = true;
            u.RoleEdit = true;
            u.RoleAdmin = false;
        });

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creator.Token);

        // 2. Create job
        var step = new CreateIntegrationJobStepRequest(
            OrderIndex: 0,
            SourceType: Contracts.Connectors.ConnectorType.Csv,
            SourceCsv: new CsvConnectorSettingsDto("/tmp/in.csv", ","),
            DestinationType: Contracts.Connectors.ConnectorType.Csv,
            DestinationCsv: new CsvConnectorSettingsDto("/tmp/out.csv", ",")
        );
        var createJobReq = new CreateIntegrationJobRequest(
            Name: "Creator Test Job",
            Steps: new List<CreateIntegrationJobStepRequest> { step },
            IsEnabled: true,
            CronExpression: null,
            OwnerOrganizationId: creator.OrgId
        );
        var jobResponse = await client.PostAsJsonAsync("api/integration-jobs", createJobReq);
        jobResponse.EnsureSuccessStatusCode();
        var createdJob = await jobResponse.Content.ReadFromJsonAsync<IntegrationJobDto>();
        createdJob.Should().NotBeNull();

        // 3. Verify creator is owner
        var ownersResponse = await client.GetAsync($"api/integration-jobs/{createdJob!.Id}/owners");
        ownersResponse.EnsureSuccessStatusCode();
        var owners = await ownersResponse.Content.ReadFromJsonAsync<IReadOnlyList<IntegrationJobOwnerDto>>();
        owners.Should().NotBeNull();
        owners.Should().ContainSingle(o => o.UserId == creator.UserId && o.IsCreator);

        // 4. Try to remove creator -> Should return BadRequest
        var removeResponse = await client.DeleteAsync($"api/integration-jobs/{createdJob.Id}/owners/{creator.UserId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OwnerCanInviteGuest_AndGuestPermissionsAreEnforced()
    {
        // 1. Create creator user and guest user
        var creator = await AuthenticateUserAsync("job_owner_1", u => { u.RoleCreate = true; u.RoleEdit = true; });
        var guestUser = await AuthenticateUserAsync("invited_guest_1", u => { u.RoleCreate = false; u.RoleEdit = false; u.RoleRead = true; });

        Guid groupId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var group = new Group
            {
                Id = Guid.NewGuid(),
                Name = "Creator Group 1",
                OrganizationId = creator.OrgId,
                CreatorUserId = creator.UserId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Groups.Add(group);
            
            // Assign creator to this group
            var dbCreator = await db.UserAccounts.FirstAsync(u => u.Id == creator.UserId);
            dbCreator.GroupId = group.Id;
            db.UserAccounts.Update(dbCreator);
            
            await db.SaveChangesAsync();
            groupId = group.Id;
        }

        var creatorClient = _factory!.CreateClient();
        creatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creator.Token);

        // 2. Create job
        var step = new CreateIntegrationJobStepRequest(
            OrderIndex: 0,
            SourceType: Contracts.Connectors.ConnectorType.Csv,
            SourceCsv: new CsvConnectorSettingsDto("/tmp/in.csv", ","),
            DestinationType: Contracts.Connectors.ConnectorType.Csv,
            DestinationCsv: new CsvConnectorSettingsDto("/tmp/out.csv", ",")
        );
        var createJobReq = new CreateIntegrationJobRequest(
            Name: "Guest Test Job",
            Steps: new List<CreateIntegrationJobStepRequest> { step },
            IsEnabled: true,
            CronExpression: null,
            OwnerOrganizationId: creator.OrgId,
            OwnerGroupId: groupId,
            GroupIds: new List<Guid> { groupId }
        );
        var jobResponse = await creatorClient.PostAsJsonAsync("api/integration-jobs", createJobReq);
        jobResponse.EnsureSuccessStatusCode();
        var createdJob = await jobResponse.Content.ReadFromJsonAsync<IntegrationJobDto>();
        createdJob.Should().NotBeNull();

        // 3. Guest tries to read the job -> Forbidden (not in group/org/ownership)
        var guestClient = _factory.CreateClient();
        guestClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guestUser.Token);
        var readResponseBefore = await guestClient.GetAsync("api/integration-jobs");
        readResponseBefore.EnsureSuccessStatusCode();
        var jobsBefore = await readResponseBefore.Content.ReadFromJsonAsync<IReadOnlyList<IntegrationJobDto>>();
        jobsBefore.Should().NotContain(j => j.Id == createdJob!.Id);

        // 4. Owner invites guest with ReadOnly permissions
        var inviteReq = new InviteGuestRequest("invited_guest_1", CanRead: true, CanEdit: false, CanCreateConnectors: false);
        var inviteResponse = await creatorClient.PostAsJsonAsync($"api/integration-jobs/{createdJob!.Id}/guests", inviteReq);
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. Guest tries to read the job -> Success!
        var readResponseAfter = await guestClient.GetAsync("api/integration-jobs");
        readResponseAfter.EnsureSuccessStatusCode();
        var jobsAfter = await readResponseAfter.Content.ReadFromJsonAsync<IReadOnlyList<IntegrationJobDto>>();
        jobsAfter.Should().Contain(j => j.Id == createdJob.Id);

        // 6. Guest tries to run the job -> Forbidden (no edit permission)
        var runResponseBefore = await guestClient.PostAsync($"api/integration-jobs/{createdJob.Id}/run", null);
        runResponseBefore.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 7. Owner updates guest to CanEdit = true
        var updatePermsReq = new UpdateGuestPermissionsRequest(CanRead: true, CanEdit: true, CanCreateConnectors: false);
        var updateResponse = await creatorClient.PutAsJsonAsync($"api/integration-jobs/{createdJob.Id}/guests/{guestUser.UserId}", updatePermsReq);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 8. Guest tries to run the job -> Success!
        var runResponseAfter = await guestClient.PostAsync($"api/integration-jobs/{createdJob.Id}/run", null);
        runResponseAfter.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GuestWithCanCreateConnectors_CanCreateConnectors()
    {
        // 1. Create creator user and guest user (who has RoleCreate = false)
        var creator = await AuthenticateUserAsync("job_owner_2", u => { u.RoleCreate = true; u.RoleEdit = true; });
        var guestUser = await AuthenticateUserAsync("invited_guest_2", u => { u.RoleCreate = false; u.RoleEdit = false; u.RoleRead = true; });

        var creatorClient = _factory!.CreateClient();
        creatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creator.Token);

        // Create job
        var step = new CreateIntegrationJobStepRequest(
            OrderIndex: 0,
            SourceType: Contracts.Connectors.ConnectorType.Csv,
            SourceCsv: new CsvConnectorSettingsDto("/tmp/in.csv", ","),
            DestinationType: Contracts.Connectors.ConnectorType.Csv,
            DestinationCsv: new CsvConnectorSettingsDto("/tmp/out.csv", ",")
        );
        var createJobReq = new CreateIntegrationJobRequest(
            Name: "Guest Connector Job",
            Steps: new List<CreateIntegrationJobStepRequest> { step },
            IsEnabled: true,
            CronExpression: null,
            OwnerOrganizationId: creator.OrgId
        );
        var jobResponse = await creatorClient.PostAsJsonAsync("api/integration-jobs", createJobReq);
        jobResponse.EnsureSuccessStatusCode();
        var createdJob = await jobResponse.Content.ReadFromJsonAsync<IntegrationJobDto>();
        createdJob.Should().NotBeNull();

        // 2. Guest tries to create connector -> Forbidden (RoleCreate = false and not guest with CanCreateConnectors yet)
        var guestClient = _factory.CreateClient();
        guestClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guestUser.Token);
        var createConnectorReq = new CreateConnectorRequest(
            Name: "Guest Standalone Connector",
            Type: Contracts.Connectors.ConnectorType.Csv,
            Csv: new CsvConnectorSettingsDto("/tmp/sample.csv", ",")
        );
        var connResponseBefore = await guestClient.PostAsJsonAsync("api/connectors", createConnectorReq);
        connResponseBefore.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Invite guest with CanCreateConnectors = true
        var inviteReq = new InviteGuestRequest("invited_guest_2", CanRead: true, CanEdit: false, CanCreateConnectors: true);
        var inviteResponse = await creatorClient.PostAsJsonAsync($"api/integration-jobs/{createdJob!.Id}/guests", inviteReq);
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Guest tries to create connector -> Success!
        var connResponseAfter = await guestClient.PostAsJsonAsync("api/connectors", createConnectorReq);
        connResponseAfter.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
