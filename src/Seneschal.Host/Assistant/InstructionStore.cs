using System.Reflection;
using Seneschal.Core;

namespace Seneschal;

public sealed record InstructionInfo(string Id, string Title, string Description, string Resource);

/// <summary>
/// The texts that tell the assistant how to behave: who it is, how it makes an extension, the design rules it follows, the SDK it builds on. They ship
/// inside the app as defaults and the person can change any of them in Options, Assistant: a changed text is kept in a file in the data folder and
/// used from the next message on, and can be put back to the default. Read each time it is needed, so no restart is involved.
/// </summary>
public sealed class InstructionStore
{
    public static IReadOnlyList<InstructionInfo> All { get; } =
    [
        new("character", "Character", "Who the assistant is and how it talks. Sent with every message", "instruction-character.md"),
        new("lessons", "Lessons", "What the assistant learned from being corrected. It adds to this itself, and you can edit or clear it. Sent with every message", "instruction-lessons.md"),
        new("guide", "Extension Guide", "What it asks, builds and tests when you ask it to make an extension. {{build}} and {{root}} are filled in when it is read", "instruction-guide.md"),
        new("ui-guidelines", "UI Guidelines", "The design rules it follows for the pages it makes, and what the check compares against", "ui-guidelines.md"),
        new("sdk-reference", "SDK Reference", "The types an extension can use", "sdk-reference.md"),
    ];

    private static string Folder => Path.Combine(AppPaths.DataDir, "assistant", "instructions");

    public event Action? Changed;

    public static InstructionInfo Info(string id) =>
        All.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"There is no instruction '{id}'");

    private static string PathOf(string id) => Path.Combine(Folder, Info(id).Id + ".md");

    /// <summary>The person's text when there is one, otherwise the default that ships with the app.</summary>
    public string Get(string id)
    {
        try
        {
            if (File.Exists(PathOf(id)) && File.ReadAllText(PathOf(id)) is { Length: > 0 } custom) return custom;
        }
        catch (IOException)
        {
            // An unreadable file falls back to the default: the assistant must never be left without instructions
        }
        return Default(id);
    }

    public static string Default(string id)
    {
        var resource = Info(id).Resource;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The text {resource} is not part of this build");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>The lessons that have been written down, with a short lead. Empty when there are none.</summary>
    public string Lessons()
    {
        var lines = Get("lessons").Split('\n').Where(l => l.TrimStart().StartsWith('-')).ToList();
        return lines.Count == 0 ? "" : "# Lessons you wrote down\nThese come from corrections you got before. Follow them.\n" + string.Join('\n', lines);
    }

    /// <summary>Adds one dated line to the lessons.</summary>
    public void AddLesson(string lesson)
    {
        var line = string.Join(' ', lesson.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Set("lessons", Get("lessons").TrimEnd() + $"\n- {DateTime.Now:yyyy-MM-dd}: {line}\n");
    }

    public bool IsCustom(string id) => File.Exists(PathOf(id));

    /// <summary>Saves the person's text. Text that is the same as the default is not kept as a change.</summary>
    public void Set(string id, string text)
    {
        var normal = text.Replace("\r\n", "\n");
        if (string.IsNullOrWhiteSpace(normal) || normal.TrimEnd() == Default(id).TrimEnd())
        {
            Reset(id);
            return;
        }

        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathOf(id), normal, new System.Text.UTF8Encoding(false));
        Changed?.Invoke();
    }

    public void Reset(string id)
    {
        try
        {
            if (File.Exists(PathOf(id))) File.Delete(PathOf(id));
        }
        catch (IOException)
        {
            // Left as it is; the next try will do
        }
        Changed?.Invoke();
    }
}
