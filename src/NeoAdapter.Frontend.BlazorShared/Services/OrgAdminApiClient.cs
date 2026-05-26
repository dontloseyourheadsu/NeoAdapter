using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Auth;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public sealed class OrgAdminApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<OrganizationUserDto>> GetUsersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<IReadOnlyList<OrganizationUserDto>>("api/org-admin/users", cancellationToken);
            return response ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<OrganizationGroupDto>> GetGroupsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<IReadOnlyList<OrganizationGroupDto>>("api/org-admin/groups", cancellationToken);
            return response ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task UpdateUserRolesAsync(Guid userId, UpdateUserRolesRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PutAsJsonAsync($"api/org-admin/users/{userId}/roles", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Failed to update user roles." : error.Trim());
        }
    }
}
