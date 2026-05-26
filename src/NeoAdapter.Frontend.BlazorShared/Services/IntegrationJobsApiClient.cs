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
}
