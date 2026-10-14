using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seneschal.Updates;

/// <summary>Asks GitHub for the newest release. Pre-releases are never offered: they are not what "latest" means.</summary>
public static class UpdateChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20), DefaultRequestHeaders = { { "User-Agent", "seneschal-updater" } } };

    public static string Repo => Environment.GetEnvironmentVariable("SENESCHAL_UPDATE_REPO") is { Length: > 0 } repo ? repo : "araafario/seneschal";

    /// <summary>SENESCHAL_UPDATE_URL replaces GitHub with any address that answers the same JSON (a mirror, or a test).</summary>
    public static string FeedUrl => Environment.GetEnvironmentVariable("SENESCHAL_UPDATE_URL") is { Length: > 0 } url
        ? url
        : $"https://api.github.com/repos/{Repo}/releases/latest";

    public static string Rid => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    /// <summary>The newest release, or null when there is none, it cannot be read or GitHub cannot be reached.</summary>
    public static async Task<ReleaseInfo?> LatestAsync(CancellationToken cancel = default)
    {
        try
        {
            var node = JsonNode.Parse(await Http.GetStringAsync(FeedUrl, cancel));
            var tag = node?["tag_name"]?.GetValue<string>();
            if (Versions.Parse(tag) is not { } version) return null;

            string? Asset(Func<string, bool> match) => (node!["assets"] as JsonArray)?
                .Select(a => (Name: a?["name"]?.GetValue<string>(), Url: a?["browser_download_url"]?.GetValue<string>()))
                .FirstOrDefault(a => a.Name is not null && match(a.Name)).Url;

            return new ReleaseInfo(version, tag!, node!["html_url"]?.GetValue<string>() ?? $"https://github.com/{Repo}/releases",
                Asset(name => name.StartsWith("Seneschal-", StringComparison.OrdinalIgnoreCase) && name.EndsWith($"-{Rid}.zip", StringComparison.OrdinalIgnoreCase)),
                Asset(name => name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static Task<Stream> OpenAsync(string url, CancellationToken cancel) => Http.GetStreamAsync(url, cancel);

    internal static Task<string> TextAsync(string url, CancellationToken cancel) => Http.GetStringAsync(url, cancel);
}
