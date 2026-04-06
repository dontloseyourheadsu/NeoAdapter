using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Frontend.Services;

public sealed class ConnectorApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ConnectorDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetFromJsonAsync("api/connectors", NeoAdapterJsonTypeInfo.For<IReadOnlyList<ConnectorDto>>(), cancellationToken);
        return response ?? [];
    }

    public async Task<ConnectorDto?> CreateAsync(CreateConnectorRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/connectors", request, NeoAdapterJsonTypeInfo.For<CreateConnectorRequest>(), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(NeoAdapterJsonTypeInfo.For<ConnectorDto>(), cancellationToken);
    }

    public async Task<TestConnectorResponse?> TestAsync(TestConnectorRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/connectors/test", request, NeoAdapterJsonTypeInfo.For<TestConnectorRequest>(), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(NeoAdapterJsonTypeInfo.For<TestConnectorResponse>(), cancellationToken);
    }
}
