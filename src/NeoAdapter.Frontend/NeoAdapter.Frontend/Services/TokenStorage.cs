using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace NeoAdapter.Frontend.Services;

public sealed record StoredTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    bool IsAdmin = false);

public sealed class TokenStorage
{
    private static string GetFilePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoAdapter");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return Path.Combine(folder, "tokens.json");
    }

    public async Task SaveAsync(StoredTokens tokens)
    {
        try
        {
            var json = JsonSerializer.Serialize(tokens, NeoAdapterJsonSerializerContext.Default.StoredTokens);
            await File.WriteAllTextAsync(GetFilePath(), json);
        }
        catch
        {
            // Ignore storage errors in browser/restricted environments
        }
    }

    public async Task<StoredTokens?> LoadAsync()
    {
        try
        {
            var path = GetFilePath();
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize(json, NeoAdapterJsonSerializerContext.Default.StoredTokens);
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
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
    }
}
