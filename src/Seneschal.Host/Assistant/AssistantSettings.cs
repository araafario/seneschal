using System.Text.Json;
using Seneschal.Core;

namespace Seneschal;

/// <summary>The models the assistant offers. Claude Code takes these short names and picks the newest version of each; an empty value leaves the choice to Claude Code.</summary>
public static class AssistantModels
{
    public sealed record Choice(string Value, string Label);

    private static readonly Choice[] Known =
    [
        new("", "Default"),
        new("opus", "Opus"),
        new("sonnet", "Sonnet"),
        new("haiku", "Haiku"),
    ];

    /// <summary>The known models, plus the current value when the user typed one of their own in an earlier version.</summary>
    public static IReadOnlyList<Choice> Choices(string? current) =>
        string.IsNullOrEmpty(current) || Known.Any(k => k.Value == current) ? Known : [.. Known, new Choice(current, current)];

    public static string Label(string? value) => Choices(value).First(c => c.Value == (value ?? "")).Label;
}

/// <summary>How the assistant runs: which Claude to start, whether to ask before every change, and the secret that lets Claude into the MCP endpoint.</summary>
public sealed class AssistantSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Saved(string? ClaudePath, bool AskBeforeChanges, string? Model);

    private readonly object _gate = new();
    private string? _claudePath;
    private string? _model;
    private bool _askBeforeChanges;

    public AssistantSettings()
    {
        try
        {
            if (File.Exists(AppPaths.File("assistant.json")) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(AppPaths.File("assistant.json"))) is { } saved)
            {
                _claudePath = saved.ClaudePath;
                _askBeforeChanges = saved.AskBeforeChanges;
                _model = saved.Model;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Start with the defaults
        }

        Secret = LoadSecret();
    }

    /// <summary>What Claude has to send to use the MCP endpoint. Made once and kept, so a Claude set up by hand keeps working after a restart.</summary>
    public string Secret { get; }

    public string? ClaudePath { get { lock (_gate) return _claudePath; } set { Set(() => _claudePath = string.IsNullOrWhiteSpace(value) ? null : value.Trim()); } }

    public string? Model { get { lock (_gate) return _model; } set { Set(() => _model = string.IsNullOrWhiteSpace(value) ? null : value.Trim()); } }

    public bool AskBeforeChanges { get { lock (_gate) return _askBeforeChanges; } set { Set(() => _askBeforeChanges = value); } }

    public event Action? Changed;

    private void Set(Action change)
    {
        lock (_gate)
        {
            change();
            try { File.WriteAllText(AppPaths.File("assistant.json"), JsonSerializer.Serialize(new Saved(_claudePath, _askBeforeChanges, _model), Json)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Changed?.Invoke();
    }

    private static string LoadSecret()
    {
        var file = AppPaths.File("mcp-secret.txt");
        try
        {
            if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: >= 32 } existing) return existing;
        }
        catch (IOException) { }

        var secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(file, secret);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return secret;
    }
}
