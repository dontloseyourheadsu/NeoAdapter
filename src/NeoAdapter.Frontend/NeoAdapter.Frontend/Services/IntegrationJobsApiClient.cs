using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Frontend.Services;

public sealed class IntegrationJobsApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetFromJsonAsync<IReadOnlyList<IntegrationJobDto>>("api/integration-jobs", JsonOptions, cancellationToken);
        return response ?? [];
    }

    public async Task<IntegrationJobDto?> CreateAsync(CreateIntegrationJobRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/integration-jobs", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IntegrationJobDto>(JsonOptions, cancellationToken);
    }

    public async Task<EnqueueIntegrationJobResponse?> RunNowAsync(Guid integrationJobId, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync($"api/integration-jobs/{integrationJobId}/run", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<EnqueueIntegrationJobResponse>(JsonOptions, cancellationToken);
    }
}
