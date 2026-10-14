using System.Text.Json;
using System.Text.RegularExpressions;
using Seneschal.Sdk;

namespace Seneschal.Core;

/// <summary>
/// extension.json: what the app and, later, a catalog need to know about an extension without loading it.
/// <see cref="Shares"/> and <see cref="Uses"/> are reserved for extensions that build on each other and are not read yet.
/// </summary>
public sealed partial record ExtensionManifest(
    string Id,
    string Name,
    string Version,
    string? Description,
    string? Author,
    string? Entry,
    string? Sdk,
    string[] Styles,
    string[] Shares,
    string[] Uses)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The .NET version of <see cref="Version"/> (1.0.0 when it is missing or not a version).</summary>
    public Version ParsedVersion => System.Version.TryParse(Version, out var v) ? v : new Version(1, 0, 0);

    public static ExtensionManifest Parse(byte[] json)
    {
        Raw? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Raw>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ExtensionException($"extension.json is not valid JSON (line {(ex.LineNumber ?? 0) + 1}).");
        }
        if (raw is null) throw new ExtensionException("extension.json is empty.");

        var id = raw.Id?.Trim() ?? "";
        if (!ValidId().IsMatch(id))
            throw new ExtensionException($"extension.json needs an \"id\" of lowercase letters, digits, '.', '-' and '_' (got '{id}').");

        return new ExtensionManifest(
            Id: id,
            Name: string.IsNullOrWhiteSpace(raw.Name) ? id : raw.Name.Trim(),
            Version: string.IsNullOrWhiteSpace(raw.Version) ? "1.0.0" : raw.Version.Trim(),
            Description: raw.Description,
            Author: raw.Author,
            Entry: raw.Entry?.Trim(),
            Sdk: raw.Sdk?.Trim(),
            Styles: raw.Styles ?? [],
            Shares: raw.Shares ?? [],
            Uses: raw.Uses ?? []);
    }

    /// <summary>Throws when the extension was built for a different SDK than this app has.</summary>
    public void RequireCompatibleSdk()
    {
        if (string.IsNullOrEmpty(Sdk)) return;
        if (!System.Version.TryParse(Sdk, out var required))
            throw new ExtensionException($"extension.json has \"sdk\": \"{Sdk}\", which is not a version like 1.0.");

        var host = SdkInfo.Version;
        if (required.Major != host.Major)
            throw new ExtensionException($"Built for SDK {required.Major}.{required.Minor}, but this Seneschal has SDK {host.Major}.{host.Minor}: the major versions differ. Rebuild the extension against the current SDK, or use a matching Seneschal.");
        if (required.Minor > host.Minor)
            throw new ExtensionException($"Needs SDK {required.Major}.{required.Minor}, but this Seneschal only has {host.Major}.{host.Minor}. Update Seneschal.");
    }

    private sealed class Raw
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? Entry { get; set; }
        public string? Sdk { get; set; }
        public string[]? Styles { get; set; }
        public string[]? Shares { get; set; }
        public string[]? Uses { get; set; }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex ValidId();
}
