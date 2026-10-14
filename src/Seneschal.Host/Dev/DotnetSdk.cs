using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Seneschal.Dev;

public sealed record DotnetInfo(string Dotnet, Version Version);

/// <summary>
/// Finds the .NET SDK, which is what building an extension needs. An installed Seneschal has the SDK when the installer put it there (its
/// default), and a lite install has none, so the developer tools stay hidden until it is there. The result is checked again every minute,
/// so installing the SDK while the app runs is noticed without a restart.
/// </summary>
public sealed partial class DotnetSdk
{
    public const int RequiredMajor = 10;
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private DotnetInfo? _info;
    private DateTime _checkedAt = DateTime.MinValue;

    public DotnetSdk()
    {
        // Look once in the background so the first page that asks does not wait for it
        _ = Task.Run(() => Current);
    }

    /// <summary>The SDK Seneschal installs for itself, so removing Seneschal can remove it too and a system install is never touched.</summary>
    public static string OwnDirectory =>
        Environment.GetEnvironmentVariable("SENESCHAL_DOTNET_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seneschal", "dotnet");

    public DotnetInfo? Current
    {
        get
        {
            lock (_gate)
            {
                if (DateTime.UtcNow - _checkedAt > Fresh)
                {
                    _info = Locate();
                    _checkedAt = DateTime.UtcNow;
                }
                return _info;
            }
        }
    }

    public bool Available => Current is not null;

    public void Refresh()
    {
        lock (_gate) _checkedAt = DateTime.MinValue;
    }

    /// <summary>The newest SDK of the required major version that any known dotnet can run, or null.</summary>
    public static DotnetInfo? Locate()
    {
        // SENESCHAL_DOTNET=none pretends there is no SDK: the way to see a lite install, and to switch the developer tools off
        if (string.Equals(Environment.GetEnvironmentVariable("SENESCHAL_DOTNET"), "none", StringComparison.OrdinalIgnoreCase)) return null;

        DotnetInfo? best = null;
        foreach (var candidate in Candidates())
        {
            var found = Ask(candidate);
            if (found is not null && (best is null || found.Version > best.Version)) best = found;
        }
        return best;
    }

    private static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var list = new List<string?>
        {
            Environment.GetEnvironmentVariable("SENESCHAL_DOTNET"),
            Path.Combine(OwnDirectory, exe),
            "dotnet",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", exe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet", exe),
        };
        foreach (var path in list)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path)) continue;
            if (path != "dotnet" && !File.Exists(path)) continue;
            yield return path;
        }
    }

    private static DotnetInfo? Ask(string dotnet)
    {
        try
        {
            var psi = new ProcessStartInfo(dotnet, "--list-sdks")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(8000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            Version? best = null;
            foreach (Match match in SdkLine().Matches(output.GetAwaiter().GetResult()))
            {
                if (Version.TryParse(match.Groups[1].Value, out var version) && version.Major >= RequiredMajor && (best is null || version > best))
                    best = version;
            }
            return best is null ? null : new DotnetInfo(dotnet, best);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^(\d+\.\d+\.\d+)", RegexOptions.Multiline)]
    private static partial Regex SdkLine();

    /// <summary>
    /// Installs the SDK with Microsoft's own dotnet-install script into <see cref="OwnDirectory"/> (for this user only, no administrator
    /// rights) and puts that folder first on the user's PATH.
    /// </summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(Action<string> progress, CancellationToken cancel = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "seneschal-dotnet-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            progress("Downloading the install script");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var script = Path.Combine(work, "dotnet-install.ps1");
            await File.WriteAllBytesAsync(script, await http.GetByteArrayAsync("https://dot.net/v1/dotnet-install.ps1", cancel), cancel);

            progress($"Installing the .NET {RequiredMajor} SDK to {OwnDirectory}. This is a large download");
            var psi = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Channel", $"{RequiredMajor}.0", "-InstallDir", OwnDirectory, "-NoPath" })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var error = process.StandardError.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);
            if (process.ExitCode != 0)
                return (false, "The .NET SDK was not installed\n" + (await error + await output).Trim());

            AddToUserPath(OwnDirectory);
            return (Locate() is not null, Locate() is null ? "The install finished but no SDK was found" : $"The .NET {RequiredMajor} SDK is installed");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            return (false, $"The .NET SDK was not installed: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    private static void AddToUserPath(string folder)
    {
        if (Environment.GetEnvironmentVariable("SENESCHAL_NO_PATH") == "1") return;
        var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var has = current.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(p => p.TrimEnd('\\').Equals(folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (!has) Environment.SetEnvironmentVariable("Path", string.Join(';', new[] { folder, current }.Where(p => p.Length > 0)), EnvironmentVariableTarget.User);
    }
}
