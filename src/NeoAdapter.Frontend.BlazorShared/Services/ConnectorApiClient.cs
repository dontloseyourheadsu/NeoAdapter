using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public sealed class ConnectorApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ConnectorDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<IReadOnlyList<ConnectorDto>>("api/connectors", cancellationToken);
            return response ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<ConnectorDto?> CreateAsync(CreateConnectorRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/connectors", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ConnectorDto>(cancellationToken);
    }

    public async Task<TestConnectorResponse?> TestAsync(TestConnectorRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/connectors/test", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TestConnectorResponse>(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetSharePointListsAsync(string siteUrl, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"api/connectors/sharepoint/lists?siteUrl={System.Uri.EscapeDataString(siteUrl)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<string>>(cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<SharePointFieldDto>> GetSharePointFieldsAsync(string siteUrl, string listName, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"api/connectors/sharepoint/fields?siteUrl={System.Uri.EscapeDataString(siteUrl)}&listName={System.Uri.EscapeDataString(listName)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SharePointFieldDto>>(cancellationToken) ?? [];
    }
}
