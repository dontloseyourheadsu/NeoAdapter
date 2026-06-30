using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Application.Connectors;

public sealed class SharePointApiClient(
    IConfiguration configuration) : ISharePointApiClient
{
    public async Task<string> GetAccessTokenAsync(string siteUrl, CancellationToken cancellationToken)
    {
        var clientId = configuration["Authentication:Microsoft:ClientId"];
        var clientSecret = configuration["Authentication:Microsoft:ClientSecret"];
        var tenantId = configuration["Authentication:Microsoft:TenantId"] ?? "common";

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Microsoft Authentication is not configured on this server. Please check Configuration Authentication:Microsoft:ClientId / ClientSecret.");
        }

        var uri = new Uri(siteUrl);
        var host = uri.Host; // e.g. tenant.sharepoint.com
        var scope = $"https://{host}/.default";

        using var client = new HttpClient();
        var tokenRequestParams = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "grant_type", "client_credentials" },
            { "scope", scope }
        };

        var response = await client.PostAsync(
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(tokenRequestParams),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to obtain Microsoft access token: {response.StatusCode} - {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("access_token", out var accessTokenProp))
        {
            return accessTokenProp.GetString() ?? throw new InvalidOperationException("Access token was not returned in Microsoft OAuth response.");
        }

        throw new InvalidOperationException("Access token was not returned in Microsoft OAuth response.");
    }

    public async Task<IReadOnlyList<string>> GetListsAsync(string siteUrl, string accessToken, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json;odata=verbose");

        var uri = new Uri(siteUrl);
        var absolutePath = uri.AbsolutePath.TrimEnd('/');
        var targetUrl = $"{uri.Scheme}://{uri.Host}{absolutePath}/_api/web/lists?$filter=Hidden eq false";

        var response = await client.GetAsync(targetUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to fetch lists: {response.StatusCode} - {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var lists = new List<string>();

        if (doc.RootElement.TryGetProperty("d", out var dProp) && dProp.TryGetProperty("results", out var resultsProp))
        {
            foreach (var listObj in resultsProp.EnumerateArray())
            {
                if (listObj.TryGetProperty("Title", out var titleProp))
                {
                    var title = titleProp.GetString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        lists.Add(title);
                    }
                }
            }
        }

        return lists;
    }

    public async Task<IReadOnlyList<SharePointFieldDto>> GetFieldsAsync(string siteUrl, string listName, string accessToken, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json;odata=verbose");

        var uri = new Uri(siteUrl);
        var absolutePath = uri.AbsolutePath.TrimEnd('/');
        var targetUrl = $"{uri.Scheme}://{uri.Host}{absolutePath}/_api/web/lists/GetByTitle('{Uri.EscapeDataString(listName)}')/fields?$filter=Hidden eq false";

        var response = await client.GetAsync(targetUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to fetch fields for list '{listName}': {response.StatusCode} - {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var fields = new List<SharePointFieldDto>();

        if (doc.RootElement.TryGetProperty("d", out var dProp) && dProp.TryGetProperty("results", out var resultsProp))
        {
            foreach (var fieldObj in resultsProp.EnumerateArray())
            {
                var title = fieldObj.TryGetProperty("Title", out var t) ? t.GetString() : null;
                var internalName = fieldObj.TryGetProperty("InternalName", out var i) ? i.GetString() : null;
                var typeAsString = fieldObj.TryGetProperty("TypeAsString", out var typeProp) ? typeProp.GetString() : null;

                if (!string.IsNullOrEmpty(internalName))
                {
                    fields.Add(new SharePointFieldDto(title ?? internalName, internalName, typeAsString ?? "Text"));
                }
            }
        }

        return fields;
    }
}
