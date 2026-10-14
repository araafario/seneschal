using System.IO.Compression;
using System.Security.Cryptography;
using Seneschal.Cli;
using Seneschal.Core;

namespace Seneschal.Updates;

/// <summary>
/// Downloads a release next to the data, checks it and puts it in place. A running program cannot have its files overwritten but it can have them
/// renamed, so every file is moved aside (name.old-N) and the new one copied in. That works while Seneschal runs, and a failure half way puts
/// everything back. The old files are deleted the next time the app starts.
/// </summary>
public static class UpdateInstaller
{
    private const string ReadyMarker = "ready";

    public static string UpdatesDir => Path.Combine(AppPaths.DataDir, "updates");

    public static string InstallDir { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static Version Current => Versions.Parse(EngineMode.Version) ?? new Version(0, 0, 0);

    /// <summary>Only a copy made by the installer updates itself: not one built from source, which has no version and no launcher beside it.</summary>
    public static bool CanUpdate => EngineMode.Version != "0.0.0-dev" && File.Exists(Path.Combine(InstallDir, "SeneschalApp.exe"));

    /// <summary>Downloads and checks a release, and leaves its files ready. Returns false when it could not be done, with the reason.</summary>
    public static async Task<(bool Ok, string Message)> StageAsync(ReleaseInfo release, CancellationToken cancel = default)
    {
        if (release.ZipUrl is null) return (false, $"Release {release.Text} has no build for {UpdateChecker.Rid}");
        if (release.SumsUrl is null) return (false, $"Release {release.Text} has no checksum file, so it is not installed");

        var folder = Path.Combine(UpdatesDir, release.Text);
        if (File.Exists(Path.Combine(folder, ReadyMarker))) return (true, "Already downloaded");

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);

            var zip = Path.Combine(folder, "package.zip");
            await using (var download = await UpdateChecker.OpenAsync(release.ZipUrl, cancel))
            await using (var file = File.Create(zip))
                await download.CopyToAsync(file, cancel);

            var sums = await UpdateChecker.TextAsync(release.SumsUrl, cancel);
            var name = Path.GetFileName(new Uri(release.ZipUrl).LocalPath);
            var expected = sums.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.EndsWith(name, StringComparison.OrdinalIgnoreCase))?.Split(' ', 2)[0];
            string actual;
            await using (var file = File.OpenRead(zip))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));
            if (expected is null || !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(folder, recursive: true);
                return (false, "The download does not match its checksum");
            }

            ZipFile.ExtractToDirectory(zip, Path.Combine(folder, "files"));
            File.Delete(zip);
            if (!File.Exists(Path.Combine(folder, "files", "Seneschal.exe")))
            {
                Directory.Delete(folder, recursive: true);
                return (false, "The package has no Seneschal.exe");
            }

            File.WriteAllText(Path.Combine(folder, ReadyMarker), release.Text);
            return (true, "Downloaded");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (IOException) { }
            return (false, ex.Message);
        }
    }

    /// <summary>The newest downloaded release that is newer than this one, or null.</summary>
    public static (Version Version, string Files)? Staged()
    {
        if (!Directory.Exists(UpdatesDir)) return null;
        var newest = Directory.GetDirectories(UpdatesDir)
            .Where(d => File.Exists(Path.Combine(d, ReadyMarker)) && Directory.Exists(Path.Combine(d, "files")))
            .Select(d => new { Version = Versions.Parse(Path.GetFileName(d)), Files = Path.Combine(d, "files") })
            .Where(s => s.Version is not null && s.Version > Current)
            .OrderByDescending(s => s.Version)
            .FirstOrDefault();
        // An anonymous type is null when there is none. (A tuple would come back as (null, null) and look like an update.)
        return newest is null ? null : (newest.Version!, newest.Files);
    }

    /// <summary>Puts the downloaded release in place. Returns its version, or null when there was nothing to install or it could not be done.</summary>
    public static Version? ApplyStaged()
    {
        if (!CanUpdate || Staged() is not { } staged) return null;

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var moved = new List<(string Target, string Old)>();
        var copied = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(staged.Files, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(InstallDir, Path.GetRelativePath(staged.Files, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    var old = $"{target}.old-{stamp}";
                    File.Move(target, old);
                    moved.Add((target, old));
                }
                File.Copy(file, target);
                copied.Add(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Put back what was there
            foreach (var target in copied) try { File.Delete(target); } catch (IOException) { }
            foreach (var (target, old) in moved) try { if (File.Exists(old)) File.Move(old, target, overwrite: true); } catch (IOException) { }
            return null;
        }

        try { Directory.Delete(Path.GetDirectoryName(staged.Files)!, recursive: true); } catch (IOException) { }
        return staged.Version;
    }

    /// <summary>Removes the files an update moved aside. The ones still in use stay for next time.</summary>
    public static void CleanOld()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(InstallDir, "*.old-*", SearchOption.AllDirectories))
                try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to clean
        }
    }
}
