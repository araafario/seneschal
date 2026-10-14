using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Seneschal.Core;

namespace Seneschal.Cli;

/// <summary>
/// The app running hidden in the background, started by <c>seneschal start</c>: no window, its output in a log file, a way for
/// <c>seneschal stop</c> to ask it to shut down, and a restart that starts a new copy of itself (nothing supervises it).
/// Switched on by the --engine argument, which the start command gives. From source (dotnet run, dev.ps1) none of this applies.
/// </summary>
public static class EngineMode
{
    public const string Variable = "SENESCHAL_ENGINE";

    /// <summary>
    /// Every option the hidden app is started with is written as --name=value. The framework reads "--name value" as a pair, so a flag written on its
    /// own would take the next option as its value.
    /// </summary>
    public const string Argument = "--engine=true";
    public const string TokenHeader = "X-Seneschal-Engine";
    private const long MaxLogBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Set by <c>--engine</c> on the command line (or SENESCHAL_ENGINE=1). It is an argument, not an environment variable, because the engine is started
    /// without inheriting anything from the process that starts it: see <see cref="EngineCli"/>.
    /// </summary>
    public static bool Active { get; } = Environment.GetEnvironmentVariable(Variable) == "1" || Environment.GetCommandLineArgs().Contains(Argument);

    public static string LogFile => Path.Combine(AppPaths.DataDir, "logs", "seneschal.log");

    /// <summary>Runs first, before the app is built: waits for the copy this one replaces, and moves the console output into the log file.</summary>
    public static void Prepare()
    {
        if (!Active) return;

        var commandLine = Environment.GetCommandLineArgs();
        var wait = commandLine.FirstOrDefault(a => a.StartsWith("--wait-pid=", StringComparison.Ordinal));
        if (wait is not null && int.TryParse(wait["--wait-pid=".Length..], out var previous))
        {
            try
            {
                using var old = Process.GetProcessById(previous);
                old.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // Already gone
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxLogBytes) File.Move(LogFile, LogFile + ".old", overwrite: true);
            var log = new StreamWriter(new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a log file the output simply goes nowhere
        }
    }

    /// <summary>The endpoints the commands use, and the file that says where this engine is. Does nothing outside engine mode.</summary>
    public static void Map(WebApplication app)
    {
        if (!Active) return;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var version = Version;

        bool Allowed(HttpContext http) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(http.Request.Headers[TokenHeader].ToString()), Encoding.UTF8.GetBytes(token));

        app.MapGet("/_engine/ping", (HttpContext http) =>
            Allowed(http) ? Results.Json(new { pid = Environment.ProcessId, version }) : Results.Unauthorized());

        app.MapPost("/_engine/quit", (HttpContext http, IHostApplicationLifetime lifetime) =>
        {
            if (!Allowed(http)) return Results.Unauthorized();
            lifetime.StopApplication();
            return Results.Accepted();
        }).DisableAntiforgery();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var address = app.Urls.FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
            if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var uri)) return;
            new EngineState(Environment.ProcessId, uri.Port, token, version).Write();
        });
        app.Lifetime.ApplicationStopped.Register(() => EngineState.Remove(Environment.ProcessId));
    }

    /// <summary>Starts another copy of this app that waits for this one to end first. Used by the restart button.</summary>
    public static void StartReplacement()
    {
        // The same arguments again (where to listen, the environment), then the process to wait for
        var kept = new List<string>();
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            if (!argument.StartsWith("--wait-pid=", StringComparison.Ordinal)) kept.Add(argument);
        kept.Add($"--wait-pid={Environment.ProcessId}");

        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Join(' ', kept))
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        })?.Dispose();
    }

    public static string Version =>
        (typeof(EngineMode).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
