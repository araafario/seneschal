using System.Diagnostics;

namespace Seneschal.Cli;

/// <summary>
/// Shows Seneschal. In its own window without tabs or an address bar when Edge or Chrome is installed (Edge always is on Windows 10 and 11),
/// so it feels like an app and has its own taskbar button. Otherwise in the default browser. SENESCHAL_OPEN=tab always uses a browser tab.
/// </summary>
internal static class BrowserLauncher
{
    public static void Open(string url)
    {
        // Automated runs must not open windows on the machine
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SENESCHAL_NO_SHELL"))) return;

        try
        {
            if (Environment.GetEnvironmentVariable("SENESCHAL_OPEN") != "tab" && FindAppBrowser() is { } browser)
            {
                Process.Start(new ProcessStartInfo(browser, $"--app={url}") { UseShellExecute = false })?.Dispose();
                return;
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not open a window. Go to {url}");
        }
    }

    internal static string? FindAppBrowser()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("LocalAppData"),
        }.Where(root => !string.IsNullOrEmpty(root));

        string[] relative = [@"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe"];
        return relative.SelectMany(path => roots.Select(root => Path.Combine(root!, path))).FirstOrDefault(File.Exists);
    }
}
