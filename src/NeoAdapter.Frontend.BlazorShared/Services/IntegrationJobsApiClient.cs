using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public sealed class IntegrationJobsApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<IReadOnlyList<IntegrationJobDto>>("api/integration-jobs", cancellationToken);
            return response ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<IntegrationJobDto?> CreateAsync(CreateIntegrationJobRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/integration-jobs", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IntegrationJobDto>(cancellationToken);
    }

    public async Task<EnqueueIntegrationJobResponse?> RunNowAsync(Guid integrationJobId, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync($"api/integration-jobs/{integrationJobId}/run", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<EnqueueIntegrationJobResponse>(cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationJobRunDto>> GetRunsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<IReadOnlyList<IntegrationJobRunDto>>($"api/integration-jobs/{jobId}/runs", cancellationToken);
            return response ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<JobLogsResponse?> GetLogsAsync(Guid jobId, Guid? runId, DateTimeOffset? cursor, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"api/integration-jobs/{jobId}/logs?limit={limit}";
            if (runId.HasValue)
            {
                url += $"&runId={runId.Value}";
            }
            if (cursor.HasValue)
            {
                url += $"&cursor={Uri.EscapeDataString(cursor.Value.ToString("O"))}";
            }

            var response = await httpClient.GetFromJsonAsync<JobLogsResponse>(url, cancellationToken);
            return response;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<IntegrationJobGuestDto>> GetGuestsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<IReadOnlyList<IntegrationJobGuestDto>>($"api/integration-jobs/{jobId}/guests", cancellationToken);
            return response ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> InviteGuestAsync(Guid jobId, InviteGuestRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync($"api/integration-jobs/{jobId}/guests", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateGuestPermissionsAsync(Guid jobId, Guid userId, UpdateGuestPermissionsRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PutAsJsonAsync($"api/integration-jobs/{jobId}/guests/{userId}", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveGuestAsync(Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var response = await httpClient.DeleteAsync($"api/integration-jobs/{jobId}/guests/{userId}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<IntegrationJobOwnerDto>> GetOwnersAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<IReadOnlyList<IntegrationJobOwnerDto>>($"api/integration-jobs/{jobId}/owners", cancellationToken);
            return response ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> AddOwnerAsync(Guid jobId, AddOwnerRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync($"api/integration-jobs/{jobId}/owners", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveOwnerAsync(Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var response = await httpClient.DeleteAsync($"api/integration-jobs/{jobId}/owners/{userId}", cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
