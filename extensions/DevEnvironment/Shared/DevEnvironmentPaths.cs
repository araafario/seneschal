namespace DevEnvironment.Shared;

/// <summary>
/// Where the extension keeps its files. The folder is given by the app (the extension's own data folder) when the
/// extension is set up, which happens before anything here is used.
/// </summary>
internal static class DevEnvironmentPaths
{
    private static string? _dataDir;
    private static bool _adopted;

    public static string DataDir =>
        _dataDir ?? throw new InvalidOperationException("The extension has not been set up yet.");

    public static void Initialize(string dataDir)
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        AdoptEarlierFiles(dataDir);
    }

    public static string FilePath(string name) => Path.Combine(DataDir, name);

    /// <summary>Log files of the projects the runner starts, one per project.</summary>
    public static string RunnerLogsDir => Path.Combine(DataDir, "logs");

    /// <summary>
    /// Before this was an extension, its files lived in the app's own data folder, which is two levels above the extension's folder
    /// (&lt;app data&gt;/extension-data/&lt;id&gt;). The project list, the log layout and the log history are copied over once.
    /// Nothing that already exists is overwritten, and the originals stay where they were.
    /// </summary>
    private static void AdoptEarlierFiles(string dataDir)
    {
        if (_adopted) return;
        _adopted = true;

        var appData = Path.GetFullPath(Path.Combine(dataDir, "..", ".."));
        foreach (var name in new[] { "projects.json", "runner-state.json", "logviewer.json" })
            CopyIfMissing(Path.Combine(appData, name), Path.Combine(dataDir, name));

        var earlierLogs = Path.Combine(appData, "runner", "logs");
        if (!Directory.Exists(earlierLogs)) return;
        foreach (var log in Directory.GetFiles(earlierLogs))
            CopyIfMissing(log, Path.Combine(RunnerLogsDir, Path.GetFileName(log)));
    }

    private static void CopyIfMissing(string from, string to)
    {
        if (!File.Exists(from) || File.Exists(to)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            // Share mode: a log of a running project is open for writing by that project
            using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var destination = new FileStream(to, FileMode.CreateNew, FileAccess.Write);
            source.CopyTo(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not copied: the extension starts without that file
        }
    }
}
