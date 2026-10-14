namespace Seneschal.Dev;

/// <summary>
/// True only when the app runs in the Development environment from a checkout of this repository, where the extensions live in
/// <c>extensions/</c>. Every dev action calls <see cref="Require"/> itself, so hiding the UI is never the only protection.
/// </summary>
public sealed class DevMode
{
    public const string SolutionName = "Seneschal.slnx";

    public DevMode(IHostEnvironment env)
    {
        if (!env.IsDevelopment()) return;

        var root = FindRepoRoot(env.ContentRootPath);
        if (root is null) return;

        var extensions = Path.Combine(root, "extensions");
        if (!Directory.Exists(extensions)) return;

        RepoRoot = root;
        ExtensionsRoot = extensions;
    }

    public bool Enabled => RepoRoot is not null;

    public string? RepoRoot { get; }

    /// <summary>The folder with the extension projects of this repo: the one place the Developer page works on.</summary>
    public string? ExtensionsRoot { get; }

    public string? SolutionFile => RepoRoot is null ? null : Path.Combine(RepoRoot, SolutionName);

    /// <summary>Where a build stages an extension: artifacts/extensions/&lt;id&gt;/, which the app loads as a dev extension.</summary>
    public string? DevBuildsDir => RepoRoot is null ? null : Path.Combine(RepoRoot, "artifacts", "extensions");

    public string? PackagesDir => RepoRoot is null ? null : Path.Combine(RepoRoot, "artifacts", "packages");

    public string? HostProject => RepoRoot is null ? null : Path.Combine(RepoRoot, "src", "Seneschal.Host", "Seneschal.Host.csproj");

    /// <summary>The code the app exits with to ask whoever started it (dev.ps1, or the tray launcher) to start it again.</summary>
    public const int RestartExitCode = 42;

    public void Require()
    {
        if (!Enabled)
            throw new InvalidOperationException("Developer tools are only available when running from source in the Development environment.");
    }

    /// <summary>The folder above <paramref name="start"/> that holds Seneschal.slnx, or null.</summary>
    public static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionName)))
                return dir.FullName;
        }
        return null;
    }
}
