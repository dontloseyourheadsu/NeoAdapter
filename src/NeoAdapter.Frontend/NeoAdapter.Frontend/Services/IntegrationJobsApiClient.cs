using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Frontend.Services;

public sealed class IntegrationJobsApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetFromJsonAsync("api/integration-jobs", NeoAdapterJsonTypeInfo.For<IReadOnlyList<IntegrationJobDto>>(), cancellationToken);
        return response ?? [];
    }

    public async Task<IntegrationJobDto?> CreateAsync(CreateIntegrationJobRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/integration-jobs", request, NeoAdapterJsonTypeInfo.For<CreateIntegrationJobRequest>(), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(NeoAdapterJsonTypeInfo.For<IntegrationJobDto>(), cancellationToken);
    }

    public async Task<EnqueueIntegrationJobResponse?> RunNowAsync(Guid integrationJobId, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync($"api/integration-jobs/{integrationJobId}/run", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(NeoAdapterJsonTypeInfo.For<EnqueueIntegrationJobResponse>(), cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationJobRunDto>> GetRunsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync($"api/integration-jobs/{jobId}/runs", NeoAdapterJsonTypeInfo.For<IReadOnlyList<IntegrationJobRunDto>>(), cancellationToken);
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

            var response = await httpClient.GetFromJsonAsync(url, NeoAdapterJsonTypeInfo.For<JobLogsResponse>(), cancellationToken);
            return response;
        }
        catch
        {
            return null;
        }
    }
}
