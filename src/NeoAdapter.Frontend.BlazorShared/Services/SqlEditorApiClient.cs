using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.SqlEditor;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public sealed class SqlEditorApiClient(HttpClient httpClient)
{
    public async Task<SqlSchemaResponse?> GetSchemaAsync(GetSchemaRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/sqleditor/schema", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<SqlSchemaResponse>(cancellationToken);
    }

    public async Task<QueryResultDto?> ExecuteQueryAsync(ExecuteQueryRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/sqleditor/query", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            return new QueryResultDto(
                Columns: System.Array.Empty<string>(),
                Rows: System.Array.Empty<System.Collections.Generic.List<object?>>(),
                RowsAffected: -1,
                ErrorMessage: string.IsNullOrWhiteSpace(errMessage) ? $"HTTP error: {response.StatusCode}" : errMessage
            );
        }

        return await response.Content.ReadFromJsonAsync<QueryResultDto>(cancellationToken);
    }
}
