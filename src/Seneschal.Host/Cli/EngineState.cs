using System.Text.Json;
using Seneschal.Core;

namespace Seneschal.Cli;

/// <summary>
/// What a running hidden app (the "engine") tells the commands about itself, in engine.json in the data folder:
/// where it listens and the secret that lets <c>seneschal stop</c> talk to it. Only that user can read the file.
/// </summary>
public sealed record EngineState(int Pid, int Port, string Token, string Version)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath => AppPaths.File("engine.json");

    [System.Text.Json.Serialization.JsonIgnore]
    public string Url => $"http://localhost:{Port}";

    public static EngineState? Read()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<EngineState>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Removes the file, but only when it is still this process's own (a newer engine may already have replaced it).</summary>
    public static void Remove(int pid)
    {
        try
        {
            if (Read() is { } state && state.Pid == pid) File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it: the commands check that the process still answers
        }
    }
}
