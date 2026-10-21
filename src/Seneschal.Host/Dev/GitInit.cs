using System.Diagnostics;

namespace Seneschal.Dev;

/// <summary>Makes a git repository of a new project: git init, a .gitattributes next to the .gitignore the template already wrote, and a first commit.</summary>
public static class GitInit
{
    public const string GitIgnore = "bin/\nobj/\nartifacts/\n.vs/\n.vscode/\n.idea/\n*.user\n*.suo\n";
    public const string GitAttributes = "* text=auto\n";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Whether git can be started. Looked up once.</summary>
    public static bool Available { get; } = Run(Path.GetTempPath(), "--version").Ok;

    /// <summary>Returns a note for the user when something could not be done, otherwise null. The folder is kept either way.</summary>
    public static string? Init(string folder)
    {
        if (!Available) return "Git is not installed, so no repository was made";
        File.WriteAllText(Path.Combine(folder, ".gitattributes"), GitAttributes);
        if (!Run(folder, "init", "-b", "main").Ok && !Run(folder, "init").Ok)
            return "Could not make the git repository";
        if (!Run(folder, "add", "-A").Ok)
            return "The git repository was made, but its files could not be added";
        // A commit needs the user's name and email in the git settings
        return Run(folder, "commit", "-m", "Initial commit").Ok ? null : "The git repository was made, but the first commit needs a git user name and email";
    }

    private static (bool Ok, string Output) Run(string folder, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        try
        {
            using var process = Process.Start(info);
            if (process is null) return (false, "");
            var output = process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return (false, "");
            }
            return (process.ExitCode == 0, output.Result);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "");
        }
    }
}
