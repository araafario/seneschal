using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Seneschal.Core;

namespace Seneschal;

/// <summary>One line of a saved chat. Long tool output is cut, so the file stays small.</summary>
public sealed record SavedItem(ChatKind Kind, string Text, string? ToolName, string? Input, string? Output, bool IsError);

/// <summary>A conversation as kept on disk. <see cref="SessionId"/> is Claude's own id, which lets the chat go on after the app restarted.</summary>
public sealed record SavedChat(string Id, string Title, DateTimeOffset Updated, string? SessionId, List<SavedItem> Items);

/// <summary>The past conversations with the assistant, newest first, kept in the app's data folder.</summary>
public sealed partial class ChatHistory
{
    private const int Limit = 30;
    private const int MaxOutput = 4000;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly List<SavedChat> _chats = [];

    public event Action? Changed;

    public ChatHistory()
    {
        try
        {
            var file = AppPaths.File("assistant-history.json");
            if (File.Exists(file) && JsonSerializer.Deserialize<List<SavedChat>>(File.ReadAllText(file)) is { } saved)
                _chats.AddRange(saved.OrderByDescending(c => c.Updated));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Start with an empty history
        }
    }

    public IReadOnlyList<SavedChat> All
    {
        get { lock (_gate) return [.. _chats]; }
    }

    public SavedChat? Find(string id)
    {
        lock (_gate) return _chats.FirstOrDefault(c => c.Id == id);
    }

    /// <summary>Adds the chat, or replaces the one with the same id.</summary>
    public void Save(string id, IReadOnlyList<ChatItem> items, string? sessionId)
    {
        var first = items.FirstOrDefault(i => i.Kind == ChatKind.User);
        if (first is null) return;

        var chat = new SavedChat(id, Title(first.Text), DateTimeOffset.Now, sessionId, items.Select(Copy).ToList());
        lock (_gate)
        {
            _chats.RemoveAll(c => c.Id == id);
            _chats.Insert(0, chat);
            if (_chats.Count > Limit) _chats.RemoveRange(Limit, _chats.Count - Limit);
            Write();
        }
        Changed?.Invoke();
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            _chats.RemoveAll(c => c.Id == id);
            Write();
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _chats.Clear();
            Write();
        }
        Changed?.Invoke();
    }

    private static string Title(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 60 ? line : line[..60].TrimEnd() + "…";
    }

    private const string Hidden = "[hidden]";

    private static SavedItem Copy(ChatItem item) =>
        new(item.Kind, Redact(item.Text) ?? "", item.ToolName, Redact(item.Input),
            Shorten(Redact(item.Output)), item.IsError);

    // Hidden first, then cut: a cut token would no longer look like one
    private static string? Shorten(string? text) => text is { Length: > MaxOutput } ? text[..MaxOutput] : text;

    /// <summary>
    /// A saved chat must not become a place where passwords and tokens sit in a file: the values of fields that are named like one are hidden, and so is
    /// anything that looks like a token (a JWT or a Bearer header) wherever it appears, including in what Claude wrote. The live chat still shows them.
    /// </summary>
    internal static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
        {
            try
            {
                var node = JsonNode.Parse(text);
                if (node is not null)
                {
                    HideFields(node);
                    text = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (JsonException)
            {
                // Not JSON after all: the patterns below still apply
            }
        }

        text = Bearer().Replace(text, "Bearer " + Hidden);
        return Jwt().Replace(text, Hidden);
    }

    private static void HideFields(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (SensitiveName().IsMatch(key) && obj[key] is JsonValue) obj[key] = Hidden;
                    else if (obj[key] is { } child) HideFields(child);
                }
                break;
            case JsonArray array:
                foreach (var child in array) if (child is not null) HideFields(child);
                break;
        }
    }

    [GeneratedRegex("pass(word)?|secret|token|authorization|header|credential|apikey|api_key", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]{16,}=*", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    private void Write()
    {
        try { File.WriteAllText(AppPaths.File("assistant-history.json"), JsonSerializer.Serialize(_chats, Json)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
