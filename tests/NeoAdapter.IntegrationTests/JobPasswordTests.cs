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

public class JobPasswordTests : IClassFixture<DbFixture>, IAsyncLifetime
{
    private readonly DbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public JobPasswordTests(DbFixture fixture)
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
    public async Task JobPasswordFlow_FullLifecycle()
    {
        // 1. Authenticate Creator and Guest
        var creator = await AuthenticateUserAsync("password_creator_user", u =>
        {
            u.RoleCreate = true;
            u.RoleEdit = true;
            u.RoleAdmin = false;
        });

        var guest = await AuthenticateUserAsync("password_guest_user", u =>
        {
            u.RoleCreate = true;
            u.RoleEdit = true;
            u.RoleAdmin = false;
        });

        // Ensure guest belongs to the same organization to see the job in the list
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
            var dbGuest = await db.UserAccounts.FirstAsync(u => u.Id == guest.UserId);
            dbGuest.OrganizationId = creator.OrgId;
            db.UserAccounts.Update(dbGuest);
            await db.SaveChangesAsync();
        }

        var creatorClient = _factory!.CreateClient();
        creatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creator.Token);

        var guestClient = _factory!.CreateClient();
        guestClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guest.Token);

        // 2. Create password-protected job as Creator
        var step = new CreateIntegrationJobStepRequest(
            OrderIndex: 0,
            SourceType: Contracts.Connectors.ConnectorType.Csv,
            SourceCsv: new CsvConnectorSettingsDto("/tmp/in.csv", ","),
            DestinationType: Contracts.Connectors.ConnectorType.Csv,
            DestinationCsv: new CsvConnectorSettingsDto("/tmp/out.csv", ",")
        );
        var createJobReq = new CreateIntegrationJobRequest(
            Name: "Protected Job",
            Steps: new List<CreateIntegrationJobStepRequest> { step },
            IsEnabled: true,
            CronExpression: null,
            OwnerOrganizationId: creator.OrgId,
            Password: "JobPassword123!"
        );

        var createResponse = await creatorClient.PostAsJsonAsync("api/integration-jobs", createJobReq);
        createResponse.EnsureSuccessStatusCode();
        var createdJob = await createResponse.Content.ReadFromJsonAsync<IntegrationJobDto>();
        createdJob.Should().NotBeNull();
        createdJob!.IsPasswordProtected.Should().BeTrue();
        createdJob.IsUnlocked.Should().BeTrue(); // Creator should have immediate access

        // 3. Guest list jobs -> Should see job, but IsUnlocked should be false
        var guestListResponse = await guestClient.GetAsync("api/integration-jobs");
        guestListResponse.EnsureSuccessStatusCode();
        var jobsList = await guestListResponse.Content.ReadFromJsonAsync<IReadOnlyList<IntegrationJobDto>>();
        jobsList.Should().NotBeNull();
        var guestJobDto = jobsList!.First(j => j.Id == createdJob.Id);
        guestJobDto.IsPasswordProtected.Should().BeTrue();
        guestJobDto.IsUnlocked.Should().BeFalse();
        guestJobDto.Steps.Should().BeEmpty(); // Locked jobs return no steps

        // 4. Guest tries to read runs -> Should return 403 (Forbidden / Locked)
        var detailsResponse = await guestClient.GetAsync($"api/integration-jobs/{createdJob.Id}/runs");
        detailsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. Guest unlocks job with wrong password -> Should fail
        var unlockResponseFail = await guestClient.PostAsJsonAsync($"api/integration-jobs/{createdJob.Id}/unlock", new UnlockJobRequest("WrongPassword"));
        unlockResponseFail.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 6. Guest unlocks job with correct password -> Should succeed
        var unlockResponseSuccess = await guestClient.PostAsJsonAsync($"api/integration-jobs/{createdJob.Id}/unlock", new UnlockJobRequest("JobPassword123!"));
        unlockResponseSuccess.EnsureSuccessStatusCode();

        // 7. Guest reads runs again -> Should succeed (200 OK)
        var detailsResponseSuccess = await guestClient.GetAsync($"api/integration-jobs/{createdJob.Id}/runs");
        detailsResponseSuccess.EnsureSuccessStatusCode();

        var guestListResponseSuccess = await guestClient.GetAsync("api/integration-jobs");
        guestListResponseSuccess.EnsureSuccessStatusCode();
        var jobsListSuccess = await guestListResponseSuccess.Content.ReadFromJsonAsync<IReadOnlyList<IntegrationJobDto>>();
        var unlockedGuestJob = jobsListSuccess!.First(j => j.Id == createdJob.Id);
        unlockedGuestJob.IsUnlocked.Should().BeTrue();
        unlockedGuestJob.Steps.Should().NotBeEmpty();

        // 8. Guest tries to change password -> Should return 403 (Forbidden) because only creator can change password
        var changePasswordGuestRes = await guestClient.PutAsJsonAsync($"api/integration-jobs/{createdJob.Id}/password", new UpdateJobPasswordRequest("NewSecret123!"));
        changePasswordGuestRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 9. Creator changes password -> Should succeed
        var changePasswordCreatorRes = await creatorClient.PutAsJsonAsync($"api/integration-jobs/{createdJob.Id}/password", new UpdateJobPasswordRequest("NewSecret123!"));
        changePasswordCreatorRes.EnsureSuccessStatusCode();

        // 10. Guest reads runs -> Should be locked again (403 Forbidden) because changing password resets permissions
        var detailsResponsePostReset = await guestClient.GetAsync($"api/integration-jobs/{createdJob.Id}/runs");
        detailsResponsePostReset.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
