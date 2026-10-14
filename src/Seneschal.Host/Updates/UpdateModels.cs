using System.Text.Json;
using Seneschal.Core;

namespace Seneschal.Updates;

/// <summary>A published release: its version, its page and the files of the build for this machine.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string? ZipUrl, string? SumsUrl)
{
    public string Text => Version.ToString(3);
}

public static class Versions
{
    /// <summary>"v1.2.3", "1.2.3-beta.1" and "1.2.3+abc" all read as 1.2.3. Anything else is null.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = text.Trim().TrimStart('v', 'V');
        var end = clean.IndexOfAny(['-', '+']);
        if (end >= 0) clean = clean[..end];
        if (!Version.TryParse(clean, out var version)) return null;
        return new Version(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
    }

    /// <summary>A new major number can break things, so it is never installed without being asked.</summary>
    public static bool IsMajor(Version current, Version next) => next.Major > current.Major;
}

/// <summary>What the updater remembers in update.json in the data folder.</summary>
public sealed class UpdateSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>False: the updater only says that a release exists, and installs nothing by itself.</summary>
    public bool Auto { get; set; } = true;

    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>The newest release seen at the last check, so the notice survives a restart without asking GitHub again.</summary>
    public string? Latest { get; set; }

    /// <summary>How often to look for an update. SENESCHAL_UPDATE_DAYS changes it (0 checks every time).</summary>
    public static double Days => double.TryParse(Environment.GetEnvironmentVariable("SENESCHAL_UPDATE_DAYS"), System.Globalization.CultureInfo.InvariantCulture, out var days) ? days : 3;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Due => CheckedAt is null || DateTimeOffset.Now - CheckedAt >= TimeSpan.FromDays(Days);

    private static string FilePath => AppPaths.File("update.json");

    public static UpdateSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(FilePath)) is { } saved) return saved;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Start with the defaults
        }
        return new UpdateSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember is not worth stopping for
        }
    }
}
