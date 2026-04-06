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
}
