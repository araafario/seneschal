using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Seneschal.Core;
using Seneschal.Mcp;

namespace Seneschal;

public enum ChatKind
{
    User,
    Assistant,
    Tool,
    Error,
}

/// <summary>One line of the conversation: what the user said, what Claude said, a tool Claude used, or something that went wrong.</summary>
public sealed class ChatItem(ChatKind kind, string text)
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public ChatKind Kind { get; } = kind;
    public string Text { get; set; } = text;
    public string? ToolName { get; set; }
    public string? ToolUseId { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public bool Done { get; set; }
    public bool IsError { get; set; }
}

/// <summary>
/// Talks to the Claude Code that is already installed and signed in on this machine. Every message starts <c>claude -p</c> (and resumes
/// the same session after the first), with the app's MCP endpoint as its only tools, so Claude can do what the user can do in the app
/// and nothing else. The answer is read as a stream and shown as it arrives.
/// </summary>
public sealed class ClaudeChat(AssistantSettings settings, McpAddress address, ChatHistory history) : IDisposable
{
    private const string ServerName = "seneschal";

    private const string Instructions =
        "You are the assistant inside Seneschal, the user's local developer toolbelt. Use the Seneschal tools to do what the user asks: they do what the user can do in the app's pages. " +
        "You cannot read files or run commands, only use these tools. Look things up with a list tool before acting on a name. Answer in a few plain words, say what you changed, and never paste tokens or passwords unless asked.";

    private readonly object _gate = new();
    private readonly List<ChatItem> _items = [];
    private Process? _process;
    private CancellationTokenSource? _cts;

    public event Action? Changed;

    public IReadOnlyList<ChatItem> Items
    {
        get { lock (_gate) return [.. _items]; }
    }

    public bool Running { get; private set; }

    public string? SessionId { get; private set; }

    /// <summary>The id the conversation is saved under in the history.</summary>
    public string Id { get; private set; } = NewId();

    private (string? Configured, string? Path, DateTime At) _found;

    /// <summary>
    /// Where Claude Code was found, or null when it was not. The search walks every folder on PATH, which is slow when one of them is on a network
    /// drive, and the page asks on every redraw, so the answer is kept. A miss is looked up again after a few seconds, so installing Claude is noticed.
    /// </summary>
    public string? ClaudePath
    {
        get
        {
            var configured = settings.ClaudePath;
            var cached = _found;
            if (cached.At != default && cached.Configured == configured
                && (cached.Path is not null ? File.Exists(cached.Path) : DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(5)))
                return cached.Path;
            var path = Find(configured);
            _found = (configured, path, DateTime.UtcNow);
            return path;
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public static string? Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;

        var names = OperatingSystem.IsWindows() ? new[] { "claude.exe", "claude.cmd" } : ["claude"];
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        return folders.SelectMany(folder => names.Select(name => Path.Combine(folder.Trim('"'), name))).FirstOrDefault(File.Exists);
    }

    /// <summary>Starts a new conversation. The old one stays in the history.</summary>
    public void Reset()
    {
        Stop();
        lock (_gate) _items.Clear();
        SessionId = null;
        Id = NewId();
        Changed?.Invoke();
    }

    /// <summary>Goes back to a conversation from the history. Claude resumes it by its own session id when the next message is sent.</summary>
    public void Open(string id)
    {
        if (history.Find(id) is not { } chat) return;
        Stop();
        lock (_gate)
        {
            _items.Clear();
            foreach (var saved in chat.Items)
                _items.Add(new ChatItem(saved.Kind, saved.Text) { ToolName = saved.ToolName, Input = saved.Input, Output = saved.Output, IsError = saved.IsError, Done = true });
        }
        SessionId = chat.SessionId;
        Id = chat.Id;
        Changed?.Invoke();
    }

    /// <summary>Removes a conversation from the history. If it is the one on screen, the screen starts over.</summary>
    public void Forget(string id)
    {
        history.Delete(id);
        if (id == Id) Reset();
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        try { if (_process is { HasExited: false } process) process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public async Task SendAsync(string prompt)
    {
        prompt = prompt.Trim();
        if (prompt.Length == 0 || Running) return;

        var claude = ClaudePath;
        if (claude is null)
        {
            Add(new ChatItem(ChatKind.Error, "Claude Code was not found. Install it and sign in, or set its path in the assistant settings"));
            return;
        }
        var url = address.Url;
        if (url is null)
        {
            Add(new ChatItem(ChatKind.Error, "The app is not listening on a plain http address, so Claude cannot reach it. Start it with an http url"));
            return;
        }

        Add(new ChatItem(ChatKind.User, prompt));
        Running = true;
        Changed?.Invoke();
        _cts = new CancellationTokenSource();
        var cancel = _cts.Token;
        var errors = new StringBuilder();
        try
        {
            var folder = Path.Combine(AppPaths.DataDir, "assistant");
            Directory.CreateDirectory(folder);
            var config = Path.Combine(folder, "mcp.json");
            File.WriteAllText(config, new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    [ServerName] = new JsonObject
                    {
                        ["type"] = "http",
                        ["url"] = url,
                        ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {settings.Secret}" },
                    },
                },
            }.ToJsonString());

            var start = new ProcessStartInfo(claude)
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false),
            };
            foreach (var argument in new[]
            {
                "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
                "--mcp-config", config, "--strict-mcp-config",
                "--tools", "",
                // The user's own settings and plugins carry hooks (a session start hook hangs when Claude runs without a terminal) and
                // other tools that have no place here. The sign in is not part of the settings, so it still works
                "--setting-sources", "project",
                "--allowedTools", $"mcp__{ServerName}",
                "--append-system-prompt", Instructions,
            })
                start.ArgumentList.Add(argument);
            if (settings.Model is { } model) { start.ArgumentList.Add("--model"); start.ArgumentList.Add(model); }
            if (SessionId is { } session) { start.ArgumentList.Add("--resume"); start.ArgumentList.Add(session); }

            // The subscription is used, not an API key, and a Claude Code that started this app must not make this one think it is nested
            start.Environment.Remove("ANTHROPIC_API_KEY");
            start.Environment.Remove("CLAUDECODE");

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Claude did not start");
            _process = process;
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (errors) errors.AppendLine(e.Data); };
            process.BeginErrorReadLine();
            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();

            var reader = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8);
            var streamed = new Dictionary<int, ChatItem>();
            string? line;
            var raw = new StringBuilder();
            while ((line = await reader.ReadLineAsync(cancel)) is not null)
            {
                if (raw.Length < 200_000) raw.AppendLine(line);
                if (line.Length == 0 || line[0] != '{') continue;
                try { Handle(JsonNode.Parse(line)!, streamed); }
                catch (System.Text.Json.JsonException) { }
            }

            await process.WaitForExitAsync(cancel);
            // Kept for finding out why a run showed nothing
            try { File.WriteAllText(Path.Combine(folder, "last-run.txt"), $"exit {process.ExitCode}{Environment.NewLine}stderr: {errors}{Environment.NewLine}{raw}"); } catch (IOException) { }
            // Claude failed without saying why in the stream (not signed in, a bad option): show what it wrote to stderr
            if (process.ExitCode != 0 && !cancel.IsCancellationRequested && !Items.Any(i => i.Kind == ChatKind.Error && i.Done) && errors.ToString().Trim() is { Length: > 0 } text)
                Add(new ChatItem(ChatKind.Error, Shorten(text)) { Done = true });
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Add(new ChatItem(ChatKind.Error, $"Could not run Claude: {ex.Message}"));
        }
        finally
        {
            _process = null;
            Running = false;
            foreach (var item in Items.Where(i => i.Kind == ChatKind.Tool && !i.Done)) item.Done = true;
            if (Items.Any(i => i.Kind == ChatKind.Error && i.Text.StartsWith("Claude no longer has", StringComparison.Ordinal))) SessionId = null;
            history.Save(Id, Items, SessionId);
            Changed?.Invoke();
        }
    }

    private void Handle(JsonNode message, Dictionary<int, ChatItem> streamed)
    {
        switch (message["type"]?.GetValue<string>())
        {
            case "system" when message["subtype"]?.GetValue<string>() == "init":
                SessionId = message["session_id"]?.GetValue<string>() ?? SessionId;
                if (message["mcp_servers"] is JsonArray servers && servers.FirstOrDefault(s => s?["name"]?.GetValue<string>() == ServerName) is { } server
                    && server["status"]?.GetValue<string>() is { } status && status != "connected")
                    Add(new ChatItem(ChatKind.Error, $"Claude could not connect to Seneschal ({status})") { Done = true });
                break;

            case "stream_event":
                Stream(message["event"], streamed);
                break;

            case "assistant":
                foreach (var block in message["message"]?["content"] as JsonArray ?? [])
                {
                    if (block?["type"]?.GetValue<string>() != "tool_use") continue;
                    var tool = Items.LastOrDefault(i => i.ToolUseId == block["id"]?.GetValue<string>());
                    if (tool is null) { tool = NewTool(block); Add(tool); }
                    tool.Input = block["input"]?.ToJsonString(new() { WriteIndented = true });
                    Changed?.Invoke();
                }
                break;

            case "user":
                foreach (var block in message["message"]?["content"] as JsonArray ?? [])
                {
                    if (block?["type"]?.GetValue<string>() != "tool_result") continue;
                    var tool = Items.LastOrDefault(i => i.ToolUseId == block["tool_use_id"]?.GetValue<string>());
                    if (tool is null) continue;
                    tool.Output = ResultText(block["content"]);
                    tool.IsError = block["is_error"]?.GetValue<bool>() == true;
                    tool.Done = true;
                    Changed?.Invoke();
                }
                break;

            case "result":
                SessionId = message["session_id"]?.GetValue<string>() ?? SessionId;
                if (message["is_error"]?.GetValue<bool>() == true)
                    Add(new ChatItem(ChatKind.Error, Shorten(message["result"]?.GetValue<string>() ?? "Claude stopped with an error")) { Done = true });
                break;
        }
    }

    private void Stream(JsonNode? stream, Dictionary<int, ChatItem> streamed)
    {
        if (stream is null) return;
        var index = stream["index"]?.GetValue<int>() ?? 0;
        switch (stream["type"]?.GetValue<string>())
        {
            case "content_block_start":
                var block = stream["content_block"];
                if (block?["type"]?.GetValue<string>() == "text")
                {
                    streamed[index] = new ChatItem(ChatKind.Assistant, "");
                    Add(streamed[index]);
                }
                else if (block?["type"]?.GetValue<string>() == "tool_use")
                {
                    var tool = NewTool(block);
                    streamed[index] = tool;
                    Add(tool);
                }
                break;

            case "content_block_delta":
                if (streamed.TryGetValue(index, out var item) && stream["delta"]?["type"]?.GetValue<string>() == "text_delta")
                {
                    item.Text += stream["delta"]?["text"]?.GetValue<string>() ?? "";
                    Changed?.Invoke();
                }
                break;
        }
    }

    private static ChatItem NewTool(JsonNode? block)
    {
        var name = block?["name"]?.GetValue<string>() ?? "tool";
        var prefix = $"mcp__{ServerName}__";
        return new ChatItem(ChatKind.Tool, "") { ToolUseId = block?["id"]?.GetValue<string>(), ToolName = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name };
    }

    private static string ResultText(JsonNode? content) => content switch
    {
        JsonValue value => value.GetValue<string>(),
        JsonArray blocks => string.Join("\n", blocks.Select(b => b?["text"]?.GetValue<string>()).Where(t => t is not null)),
        _ => "",
    };

    private static string Shorten(string text)
    {
        // A chat from the history whose session Claude has forgotten (it is kept in Claude's own folder and can be cleaned up)
        if (text.Contains("No conversation found", StringComparison.OrdinalIgnoreCase))
            return "Claude no longer has this conversation. Your next message starts a new one";

        // Claude Code that is installed but not signed in says so in its own words; say what to do about it
        if (text.Contains("/login", StringComparison.OrdinalIgnoreCase) || text.Contains("not logged in", StringComparison.OrdinalIgnoreCase) || text.Contains("invalid api key", StringComparison.OrdinalIgnoreCase))
            return "Claude Code is not signed in. Run claude in a terminal, sign in once, then try again";

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Take(6));
    }

    private void Add(ChatItem item)
    {
        lock (_gate) _items.Add(item);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
