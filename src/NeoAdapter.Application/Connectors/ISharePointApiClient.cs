using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Application.Connectors;

public interface ISharePointApiClient
{
    Task<string> GetAccessTokenAsync(string siteUrl, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetListsAsync(string siteUrl, string accessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<SharePointFieldDto>> GetFieldsAsync(string siteUrl, string listName, string accessToken, CancellationToken cancellationToken);
}
