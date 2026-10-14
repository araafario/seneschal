using System.Diagnostics;
using System.Runtime.InteropServices;

// Starts Seneschal if it is not running and opens it, the way "seneschal open" does, without showing a console window.
// When that fails, the one place a person without a terminal sees why is a message box.
var app = FindApp();
if (app is null)
{
    Message($"Seneschal.exe was not found next to this launcher ({AppContext.BaseDirectory}).{Environment.NewLine}{Environment.NewLine}"
        + "Install Seneschal with install.ps1, or start it from the folder it was installed in.");
    return 1;
}

var start = new ProcessStartInfo(app, "open")
{
    WorkingDirectory = Path.GetDirectoryName(app)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};

try
{
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(TimeSpan.FromSeconds(90)))
    {
        process.Kill();
        Message("Seneschal did not start in time");
        return 1;
    }
    if (process.ExitCode != 0)
    {
        var text = (await errors).Trim();
        Message(text.Length > 0 ? text : (await output).Trim());
        return process.ExitCode;
    }
}
catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Message($"Seneschal did not start: {ex.Message}");
    return 1;
}

return 0;

// Next to the launcher in an install. From a checkout (the launcher run from its own build folder) the app that was built last
static string? FindApp()
{
    var beside = Path.Combine(AppContext.BaseDirectory, "Seneschal.exe");
    if (File.Exists(beside)) return beside;

    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (!File.Exists(Path.Combine(dir.FullName, "Seneschal.slnx"))) continue;
        var builds = Directory.Exists(Path.Combine(dir.FullName, "src", "Seneschal.Host", "bin"))
            ? Directory.GetFiles(Path.Combine(dir.FullName, "src", "Seneschal.Host", "bin"), "Seneschal.exe", SearchOption.AllDirectories)
            : [];
        return builds.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }
    return null;
}

static void Message(string text) => MessageBoxW(IntPtr.Zero, text, "Seneschal", 0x10);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
