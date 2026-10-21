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

/// <summary>The ways a question the assistant asks can be answered.</summary>
public static class QuestionModes
{
    public const string Ask = "ask";
    public const string Recommended = "recommended";
    public const string Timeout = "timeout";

    public static string Valid(string? mode) => mode is Recommended or Timeout ? mode : Ask;
}

/// <summary>How the assistant runs: which Claude to start, whether to ask before every change, and the secret that lets Claude into the MCP endpoint.</summary>
public sealed class AssistantSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Saved(string? ClaudePath, bool AskBeforeChanges, string? Model, bool? ShowTestBrowser = null, bool? AlwaysAllow = null, string? Dock = null, int? DockWidth = null, string? QuestionMode = null, int? QuestionSeconds = null);

    private readonly object _gate = new();
    private string? _claudePath;
    private string? _model;
    private bool _askBeforeChanges;
    private bool _showTestBrowser;
    private bool _alwaysAllow;
    private string _dock = "floating";
    private int _dockWidth;
    private string _questionMode = QuestionModes.Ask;
    private int _questionSeconds = 30;

    public AssistantSettings()
    {
        try
        {
            if (File.Exists(AppPaths.File("assistant.json")) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(AppPaths.File("assistant.json"))) is { } saved)
            {
                _claudePath = saved.ClaudePath;
                _askBeforeChanges = saved.AskBeforeChanges;
                _model = saved.Model;
                _showTestBrowser = saved.ShowTestBrowser ?? false;
                _alwaysAllow = saved.AlwaysAllow ?? false;
                _dock = saved.Dock == "sidebar" ? "sidebar" : "floating";
                _dockWidth = saved.DockWidth is >= 320 and <= 900 ? saved.DockWidth.Value : 0;
                _questionMode = QuestionModes.Valid(saved.QuestionMode);
                _questionSeconds = saved.QuestionSeconds is >= 5 and <= 600 ? saved.QuestionSeconds.Value : 30;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Start with the defaults
        }

        _secret = LoadSecret();
    }

    /// <summary>What Claude has to send to use the MCP endpoint. Made once and kept, so a Claude set up by hand keeps working after a restart.</summary>
    private volatile string _secret;

    public string Secret => _secret;

    /// <summary>Makes a new secret. Whoever used the old one (a client set up by hand) is locked out until it is given the new one.</summary>
    public void RegenerateSecret()
    {
        var secret = NewSecret();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.File("mcp-secret.txt"), secret);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The new secret still applies until the app restarts, when the old file is read again
        }
        _secret = secret;
        Changed?.Invoke();
    }

    private static string NewSecret() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public string? ClaudePath { get { lock (_gate) return _claudePath; } set { Set(() => _claudePath = string.IsNullOrWhiteSpace(value) ? null : value.Trim()); } }

    public string? Model { get { lock (_gate) return _model; } set { Set(() => _model = string.IsNullOrWhiteSpace(value) ? null : value.Trim()); } }

    public bool AskBeforeChanges { get { lock (_gate) return _askBeforeChanges; } set { Set(() => _askBeforeChanges = value); } }

    /// <summary>
    /// Whether the browser that looks at a page (the assistant's preview of an extension) shows its window. Off by default: it runs headless, with
    /// no window, so it never gets in the way. Turn it on to watch what it does.
    /// </summary>
    public bool ShowTestBrowser { get { lock (_gate) return _showTestBrowser; } set { Set(() => _showTestBrowser = value); } }

    /// <summary>
    /// Runs every action without asking, deleting and the other things that always ask included. Off by default. It applies to every MCP client,
    /// because the approval is asked in the app and not per client.
    /// </summary>
    public bool AlwaysAllow { get { lock (_gate) return _alwaysAllow; } set { Set(() => _alwaysAllow = value); } }

    /// <summary>Where the chat panel sits: <c>floating</c> (a card in the bottom right corner) or <c>sidebar</c> (docked at the right, full height, the page beside it).</summary>
    public string Dock { get { lock (_gate) return _dock; } set { Set(() => _dock = value == "sidebar" ? "sidebar" : "floating"); } }

    /// <summary>The width in pixels the user dragged the docked panel to. 0 leaves it at the default.</summary>
    public int DockWidth { get { lock (_gate) return _dockWidth; } set { Set(() => _dockWidth = value is >= 320 and <= 900 ? value : 0); } }

    /// <summary>What happens when the assistant asks a question: <c>ask</c> (waits for the answer, with no time limit), <c>recommended</c> (picks the recommended option at once) or <c>timeout</c> (waits, then picks it).</summary>
    public string QuestionMode { get { lock (_gate) return _questionMode; } set { Set(() => _questionMode = QuestionModes.Valid(value)); } }

    /// <summary>How long a question waits in the <c>timeout</c> mode, in seconds.</summary>
    public int QuestionSeconds { get { lock (_gate) return _questionSeconds; } set { Set(() => _questionSeconds = value is >= 5 and <= 600 ? value : 30); } }

    public event Action? Changed;

    /// <summary>Back to the defaults (Claude is found by itself, the default model, ask before changes). The MCP secret is not a setting and stays.</summary>
    public void ResetToDefaults()
    {
        lock (_gate)
        {
            _claudePath = null;
            _model = null;
            _askBeforeChanges = false;
            _showTestBrowser = false;
            _alwaysAllow = false;
            _dock = "floating";
            _dockWidth = 0;
            _questionMode = QuestionModes.Ask;
            _questionSeconds = 30;
            try { File.Delete(AppPaths.File("assistant.json")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Changed?.Invoke();
    }

    private void Set(Action change)
    {
        lock (_gate)
        {
            change();
            try { File.WriteAllText(AppPaths.File("assistant.json"), JsonSerializer.Serialize(new Saved(_claudePath, _askBeforeChanges, _model, _showTestBrowser, _alwaysAllow, _dock, _dockWidth, _questionMode, _questionSeconds), Json)); }
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

        var secret = NewSecret();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(file, secret);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return secret;
    }
}
