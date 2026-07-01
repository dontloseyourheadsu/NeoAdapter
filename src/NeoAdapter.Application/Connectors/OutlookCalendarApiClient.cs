using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace NeoAdapter.Application.Connectors;

public sealed class OutlookCalendarApiClient(
    IConfiguration configuration) : IOutlookCalendarApiClient
{
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var clientId = configuration["Authentication:Microsoft:ClientId"];
        var clientSecret = configuration["Authentication:Microsoft:ClientSecret"];
        var tenantId = configuration["Authentication:Microsoft:TenantId"] ?? "common";

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Microsoft Authentication is not configured on this server. Please check Configuration Authentication:Microsoft:ClientId / ClientSecret.");
        }

        var scope = "https://graph.microsoft.com/.default";

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
            throw new InvalidOperationException($"Failed to obtain Microsoft access token for Graph API: {response.StatusCode} - {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("access_token", out var accessTokenProp))
        {
            return accessTokenProp.GetString() ?? throw new InvalidOperationException("Access token was not returned in Microsoft OAuth response.");
        }

        throw new InvalidOperationException("Access token was not returned in Microsoft OAuth response.");
    }

    public async Task<IReadOnlyList<string>> GetCalendarsAsync(string microsoftUserId, string accessToken, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var targetUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(microsoftUserId)}/calendars";

        var response = await client.GetAsync(targetUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to fetch calendars: {response.StatusCode} - {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var list = new List<string>();
        if (doc.RootElement.TryGetProperty("value", out var valueProp))
        {
            foreach (var item in valueProp.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        list.Add(name);
                    }
                }
            }
        }

        return list;
    }

    public async Task CreateEventAsync(string microsoftUserId, string? calendarName, string accessToken, object eventPayload, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var targetUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(microsoftUserId)}/events";

        if (!string.IsNullOrWhiteSpace(calendarName) && !string.Equals(calendarName, "Default", StringComparison.OrdinalIgnoreCase))
        {
            // Find calendar ID first
            var listUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(microsoftUserId)}/calendars?$select=id,name";
            var listResponse = await client.GetAsync(listUrl, cancellationToken);
            if (listResponse.IsSuccessStatusCode)
            {
                var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken);
                using var listDoc = JsonDocument.Parse(listJson);
                if (listDoc.RootElement.TryGetProperty("value", out var valueProp))
                {
                    foreach (var cal in valueProp.EnumerateArray())
                    {
                        if (cal.TryGetProperty("name", out var nameProp) && 
                            string.Equals(nameProp.GetString(), calendarName, StringComparison.OrdinalIgnoreCase))
                        {
                            var calId = cal.GetProperty("id").GetString();
                            if (!string.IsNullOrEmpty(calId))
                            {
                                targetUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(microsoftUserId)}/calendars/{Uri.EscapeDataString(calId)}/events";
                                break;
                            }
                        }
                    }
                }
            }
        }

        var content = new StringContent(JsonSerializer.Serialize(eventPayload), System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(targetUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to create event in Outlook calendar: {response.StatusCode} - {err}");
        }
    }
}
