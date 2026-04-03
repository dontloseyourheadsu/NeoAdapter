using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Frontend.Services;

public sealed class ConnectorApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<ConnectorDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetFromJsonAsync<IReadOnlyList<ConnectorDto>>("api/connectors", JsonOptions, cancellationToken);
        return response ?? [];
    }

    public async Task<ConnectorDto?> CreateAsync(CreateConnectorRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/connectors", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ConnectorDto>(JsonOptions, cancellationToken);
    }
}
