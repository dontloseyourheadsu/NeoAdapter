using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Pipeline;

namespace NeoAdapter.Frontend.Services;

public sealed class PipelineEditorApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PipelineEditorResponse?> GetEditorAsync(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<PipelineEditorResponse>("api/pipeline-editor", JsonOptions, cancellationToken);
    }

    public async Task<PipelineDraft?> UpdateDraftAsync(UpdatePipelineDraftRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PutAsJsonAsync("api/pipeline-editor/draft", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<PipelineDraft>(JsonOptions, cancellationToken);
    }
}
