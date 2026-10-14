namespace Seneschal.Core;

/// <summary>Where the app keeps its files. SENESCHAL_DATA_DIR overrides the default %AppData%/Seneschal (used to isolate test runs).</summary>
public static class AppPaths
{
    /// <summary>The name the app had before it was called Seneschal. Its data folder is adopted once, so nothing the user set up is lost.</summary>
    private const string EarlierName = "DevToolbelt";

    public static string DataDir { get; } =
        Resolve(Environment.GetEnvironmentVariable("SENESCHAL_DATA_DIR"), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>Imported extension packages, one folder per extension.</summary>
    public static string ExtensionsDir => Path.Combine(DataDir, "extensions");

    /// <summary>One private folder per extension, for the files an extension keeps. Not removed when the extension is.</summary>
    public static string ExtensionDataDir => Path.Combine(DataDir, "extension-data");

    public static string File(string name) => Path.Combine(DataDir, name);

    /// <summary>
    /// The data folder under <paramref name="roamingRoot"/>. When there is none yet but one from the earlier name exists, that
    /// folder becomes the data folder: it is renamed, or copied when something in it is in use (a project the runner started
    /// keeps its log file open), in which case the original is left where it is.
    /// </summary>
    internal static string Resolve(string? custom, string roamingRoot)
    {
        if (!string.IsNullOrEmpty(custom)) return custom;

        var current = Path.Combine(roamingRoot, "Seneschal");
        var earlier = Path.Combine(roamingRoot, EarlierName);
        if (!Directory.Exists(current) && Directory.Exists(earlier))
        {
            try
            {
                Directory.Move(earlier, current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CopyAll(earlier, current);
            }
        }
        return current;
    }

    /// <summary>Copies what can be copied. A file that cannot be read right now is skipped; the app starts with whatever came across.</summary>
    private static void CopyAll(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                source.CopyTo(destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip this file
            }
        }
    }
}
