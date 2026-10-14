using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Seneschal.Core;
using Seneschal.Updates;

namespace Seneschal.Cli;

/// <summary>
/// What <c>seneschal</c> does when it is given a command instead of serving: start the app hidden, open it in the browser, stop it,
/// restart it, say whether it runs. The same Seneschal.exe does both, so there is only one file to install.
/// </summary>
public static class EngineCli
{
    public const int PreferredPort = 5380;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private const string Help = """
        Seneschal

        Usage: seneschal [command]

          open       Start it if needed and open it in the browser (the default)
          start      Start it in the background
          stop       Stop it
          restart    Stop it and start it again
          status     Show whether it is running
          logs       Show the end of the log
          update     Look for a new version and install it (update --check only looks)
          extension new <Name> [folder]   Start a new extension project
          dev        Show whether you can make extensions here (dev install adds the .NET SDK)
          doctor     Check that everything Seneschal needs is in place and say what to fix
          version    Show the version

        Projects and containers it started keep running when it stops
        """;

    /// <summary>The exit code when the arguments were a command, or null when the app should serve (a dev run, or the engine itself).</summary>
    public static int? Handle(string[] args)
    {
        var verb = args.FirstOrDefault()?.ToLowerInvariant();

        // From source (dotnet run, dev.ps1) there are no arguments and the app serves. The same goes for "run" and for any option
        // (--engine=true, --urls=... and so on), and for an app that was told where to listen. The hidden app is started with options only,
        // each as --name=value: the framework reads "--name value" and a bare word as pairs, so anything else would swallow its neighbour
        var serves = verb == "run"
            || (verb is null && (IsDevelopment || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))))
            || (verb is not null && verb.StartsWith("--", StringComparison.Ordinal) && verb is not ("--help" or "--version"));
        if (serves) return null;

        return Run(verb ?? "open", args).GetAwaiter().GetResult();
    }

    private static bool IsDevelopment =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development";

    private static async Task<int> Run(string verb, string[] args)
    {
        switch (verb)
        {
            case "open":
                return await Start(openBrowser: true);
            case "start":
                return await Start(openBrowser: false);
            case "stop":
                return await Stop(Option(args, "--from"));
            case "restart":
                if (await Stop(null) != 0) return 1;
                return await Start(openBrowser: false);
            case "status":
                return await Status();
            case "logs":
                return Logs();
            case "update":
                return await Update(args);
            case "extension":
                return NewExtension(args);
            case "dev":
                return await Dev(args);
            case "doctor":
                return await Doctor();
            case "version" or "--version" or "-v":
                Console.WriteLine($"Seneschal {EngineMode.Version}");
                return 0;
            case "help" or "--help" or "-h" or "-?":
                Console.WriteLine(Help);
                return 0;
            default:
                Console.Error.WriteLine($"Unknown command '{verb}'");
                Console.Error.WriteLine("Run seneschal help to see the commands");
                return 1;
        }
    }

    private static async Task<int> Start(bool openBrowser)
    {
        var state = await Running();
        if (state is null)
        {
            // A downloaded update goes in while nothing runs from these files
            if (UpdateInstaller.ApplyStaged() is { } updated) Console.WriteLine($"Updated to {updated.ToString(3)}");
            Console.WriteLine("Starting Seneschal");
            if (!Spawn())
            {
                Console.Error.WriteLine("Could not start it");
                return 1;
            }
            state = await WaitUntilRunning(TimeSpan.FromSeconds(60));
            if (state is null)
            {
                Console.Error.WriteLine($"It did not start. See {EngineMode.LogFile}");
                return 1;
            }
        }

        Console.WriteLine($"Seneschal is running at {state.Url}");
        if (openBrowser) BrowserLauncher.Open(state.Url);
        return 0;
    }

    /// <summary>
    /// <paramref name="fromFolder"/> limits the stop to a copy that runs from that folder. A build uses it to free its own output folder
    /// (the running copy locks the files the build wants to replace) without stopping an installed copy somewhere else.
    /// </summary>
    private static async Task<int> Stop(string? fromFolder)
    {
        if (await Running() is not { } state)
        {
            Console.WriteLine("Seneschal is not running");
            return 0;
        }

        if (fromFolder is not null && !RunsFrom(state.Pid, fromFolder))
        {
            Console.WriteLine("Seneschal is running from another folder, so it was left alone");
            return 0;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{state.Url}/_engine/quit");
            request.Headers.Add(EngineMode.TokenHeader, state.Token);
            await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The wait below decides
        }

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < until && await Running() is not null) await Task.Delay(200);

        if (await Running() is not null)
        {
            // It did not stop by itself
            try { Process.GetProcessById(state.Pid).Kill(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            EngineState.Remove(state.Pid);
        }

        Console.WriteLine("Seneschal stopped");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var at = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static bool RunsFrom(int pid, string folder)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var running = Path.GetDirectoryName(process.MainModule?.FileName);
            return running is not null && string.Equals(Path.GetFullPath(running).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static async Task<int> Status()
    {
        if (await Running() is { } state)
        {
            Console.WriteLine($"Seneschal {state.Version} is running at {state.Url} (process {state.Pid})");
            PrintUpdate();
            return 0;
        }
        Console.WriteLine("Seneschal is not running");
        PrintUpdate();
        return 1;
    }

    /// <summary>What the last check found, without asking again.</summary>
    private static void PrintUpdate()
    {
        if (UpdateInstaller.Staged() is { } staged)
        {
            Console.WriteLine($"Update {staged.Version.ToString(3)} is ready and installs the next time it starts");
            return;
        }
        if (Versions.Parse(UpdateSettings.Load().Latest) is { } latest && latest > UpdateInstaller.Current)
            Console.WriteLine(Versions.IsMajor(UpdateInstaller.Current, latest) ? $"Major update {latest.ToString(3)} available. Run seneschal update" : $"Update {latest.ToString(3)} available. Run seneschal update");
    }

    /// <summary>One line per thing Seneschal needs, with ok, a note or a problem, and what to do about it. Exit code 1 when something is wrong.</summary>
    private static async Task<int> Doctor()
    {
        var problems = 0;
        void Line(string state, string what, string? fix = null)
        {
            Console.WriteLine($"  {state,-5} {what}");
            if (fix is not null) Console.WriteLine($"        {fix}");
            if (state == "FIX") problems++;
        }

        Console.WriteLine($"Seneschal {EngineMode.Version}");
        Console.WriteLine();

        var running = await Running();
        if (running is null) Line("note", "It is not running", "Run seneschal start");
        else Line("ok", $"Running at {running.Url}");

        var data = Seneschal.Core.AppPaths.DataDir;
        try
        {
            Directory.CreateDirectory(data);
            var probe = Path.Combine(data, ".doctor");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            Line("ok", $"The data folder can be written ({data})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Line("FIX", $"The data folder cannot be written ({data}): {ex.Message}", "Choose another with the SENESCHAL_DATA_DIR variable");
        }

        var claude = Seneschal.ClaudeChat.Find(null);
        if (claude is null) Line("note", "Claude Code was not found, so the assistant panel cannot answer", "Install Claude Code and sign in, or set the path in Options, Assistant. Other MCP clients still work");
        else Line("ok", $"Claude Code found ({claude})");

        var sdk = Seneschal.Dev.DotnetSdk.Locate();
        if (sdk is null) Line("note", "The .NET SDK is missing, so the developer tools are off", "Run seneschal dev install");
        else Line("ok", $"The .NET SDK {sdk.Version} is installed");

        if (File.Exists(Path.Combine(data, "mcp-secret.txt"))) Line("ok", "The MCP secret exists");
        else Line("note", "The MCP secret is made the first time Seneschal runs");

        Console.WriteLine();
        Console.WriteLine(problems == 0 ? "Nothing needs fixing" : $"{problems} thing{(problems == 1 ? "" : "s")} to fix");
        return problems == 0 ? 0 : 1;
    }

    /// <summary>Whether the .NET SDK is there, which is what making an extension needs. "dev install" puts it there.</summary>
    private static async Task<int> Dev(string[] args)
    {
        var info = Seneschal.Dev.DotnetSdk.Locate();
        if (args.Length > 1 && args[1].Equals("install", StringComparison.OrdinalIgnoreCase))
        {
            if (info is not null)
            {
                Console.WriteLine($"The .NET SDK {info.Version} is already installed ({info.Dotnet})");
                return 0;
            }

            var result = await Seneschal.Dev.DotnetSdk.InstallAsync(Console.WriteLine);
            Console.WriteLine(result.Message);
            if (result.Ok) Console.WriteLine("Open a new terminal to use dotnet there. Seneschal notices it by itself");
            return result.Ok ? 0 : 1;
        }

        if (info is null)
        {
            Console.WriteLine("The .NET SDK is not installed, so the developer tools are off");
            Console.WriteLine("Run seneschal dev install to add it");
            return 1;
        }

        Console.WriteLine($"The .NET SDK {info.Version} is installed ({info.Dotnet})");
        Console.WriteLine("Ask the assistant: Help me create a new extension");
        return 0;
    }

    private static int NewExtension(string[] args)
    {
        if (args.Length < 3 || !args[1].Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Use seneschal extension new <Name> [folder]");
            return 1;
        }

        var folder = args.Length > 3 ? args[3] : Directory.GetCurrentDirectory();
        var result = Seneschal.Dev.Scaffolder.CreateStandalone(folder, new Seneschal.Dev.NewExtension(args[2], "", "", "", Seneschal.Dev.Scaffolder.DefaultIcon));
        if (!result.Ok)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        Console.WriteLine(result.Message);
        Console.WriteLine();
        Console.WriteLine($"  cd {result.Folder}");
        Console.WriteLine("  dotnet build");
        Console.WriteLine("Then import artifacts/packages/*.snx in Options, Extensions, or ask the assistant to help you build it");
        if (Seneschal.Dev.DotnetSdk.Locate() is null) Console.WriteLine("Building needs the .NET SDK: run seneschal dev install");
        return 0;
    }

    private static async Task<int> Update(string[] args)
    {
        if (args.Length >= 3 && args[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var on = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (!on && !args[2].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Use seneschal update auto on, or off");
                return 1;
            }
            var chosen = UpdateSettings.Load();
            chosen.Auto = on;
            chosen.Save();
            Console.WriteLine(on ? "Updates install themselves" : "Updates are only announced");
            return 0;
        }

        var latest = await UpdateChecker.LatestAsync();
        if (latest is null)
        {
            Console.Error.WriteLine("Could not reach the releases");
            return 1;
        }

        var current = UpdateInstaller.Current;
        var settings = UpdateSettings.Load();
        settings.CheckedAt = DateTimeOffset.Now;
        settings.Latest = latest.Text;
        settings.Save();

        if (latest.Version <= current)
        {
            Console.WriteLine($"Seneschal is up to date ({current.ToString(3)})");
            return 0;
        }

        Console.WriteLine($"Seneschal {latest.Text} is available, you have {current.ToString(3)}");
        if (Versions.IsMajor(current, latest.Version)) Console.WriteLine($"It is a major update and can break things. Notes: {latest.PageUrl}");
        if (args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase))) return 0;

        if (!UpdateInstaller.CanUpdate)
        {
            Console.Error.WriteLine("This copy was not made by the installer, so it cannot update itself. Run install.ps1");
            return 1;
        }

        Console.WriteLine("Downloading");
        var (ok, message) = await UpdateInstaller.StageAsync(latest);
        if (!ok)
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var wasRunning = await Running() is not null;
        if (wasRunning && await Stop(null) != 0) return 1;
        if (UpdateInstaller.ApplyStaged() is not { } applied)
        {
            Console.Error.WriteLine("Could not install it. Close what runs from the install folder and try again");
            if (wasRunning) await Start(openBrowser: false);
            return 1;
        }

        Console.WriteLine($"Updated to {applied.ToString(3)}");
        return wasRunning ? await Start(openBrowser: false) : 0;
    }

    private static int Logs()
    {
        if (!File.Exists(EngineMode.LogFile))
        {
            Console.WriteLine("There is no log yet");
            return 0;
        }

        Console.WriteLine(EngineMode.LogFile);
        Console.WriteLine();
        try
        {
            using var stream = new FileStream(EngineMode.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > 40) lines.Dequeue();
            }
            foreach (var line in lines) Console.WriteLine(line);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        return 0;
    }

    /// <summary>The engine, when the file names one and it answers with its secret.</summary>
    private static async Task<EngineState?> Running()
    {
        if (EngineState.Read() is not { } state) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{state.Url}/_engine/ping");
            request.Headers.Add(EngineMode.TokenHeader, state.Token);
            using var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode ? state : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task<EngineState?> WaitUntilRunning(TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (await Running() is { } state) return state;
            await Task.Delay(300);
        }
        return null;
    }

    private static bool Spawn()
    {
        var program = Environment.ProcessPath;
        if (program is null) return false;

        var port = PickPort(int.TryParse(Environment.GetEnvironmentVariable("SENESCHAL_PORT"), out var chosen) ? chosen : PreferredPort);
        // Started through the shell, with everything as arguments. Starting it directly would hand it every handle this process holds, including the pipe
        // of whoever reads this command's output (a script, a build): they would wait for the engine to end before they saw the end of the output. The
        // arguments win over the settings of a developer's own machine (such as Development in the environment), so the installed app always runs as itself.
        // A copy that runs from a checkout (a Seneschal.slnx above its folder: dev.ps1) runs as Development instead, which is what gives it the Developer page
        var environment = Seneschal.Dev.DevMode.FindRepoRoot(AppContext.BaseDirectory) is not null ? "Development" : "Production";
        var start = new ProcessStartInfo(program, $"{EngineMode.Argument} --urls=http://localhost:{port} --environment={environment}")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The preferred port when it is free, otherwise the next free one, so the address stays the same from one start to the next.</summary>
    private static int PickPort(int preferred)
    {
        for (var port = preferred; port < preferred + 20; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
                // In use: try the next one
            }
        }
        return preferred;
    }
}
