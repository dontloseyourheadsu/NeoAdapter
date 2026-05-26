using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NeoAdapter.Frontend.BlazorShared.Services;

namespace NeoAdapter.Frontend.MauiBlazor.Services;

public sealed class MauiTokenStorage : ITokenStorage
{
    private static string GetFilePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoAdapterBlazor");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return Path.Combine(folder, "tokens.json");
    }

    public async Task SaveAsync(string accessToken, string refreshToken, DateTimeOffset expiresAtUtc, bool isAdmin)
    {
        try
        {
            var tokens = new StoredTokens(accessToken, refreshToken, expiresAtUtc, isAdmin);
            var json = JsonSerializer.Serialize(tokens);
            await File.WriteAllTextAsync(GetFilePath(), json);
        }
        catch
        {
            // Ignore storage errors in restricted environments
        }
    }

    public async Task<StoredTokens?> LoadAsync()
    {
        try
        {
            var path = GetFilePath();
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<StoredTokens>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            var path = GetFilePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore
        }
        await Task.CompletedTask;
    }
}
