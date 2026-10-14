using System.Text.Json;
using System.Text.RegularExpressions;
using Seneschal.Core;

namespace Seneschal.Dev;

/// <summary>One thing the check found, with the file and line, so it can be fixed without searching.</summary>
public sealed record CheckFinding(string Severity, string File, int Line, string Rule, string Message);

public sealed record CheckResult(string Project, int Errors, int Warnings, int Review, IReadOnlyList<CheckFinding> Findings)
{
    /// <summary>No errors and no warnings. "Review" items are things only a person can judge, so they never fail the check.</summary>
    public bool Clean => Errors == 0 && Warnings == 0;
}

/// <summary>
/// A read-only check of an extension project against Seneschal's rules (docs/ui-guidelines.md and the SDK): what a script can see, so that what is
/// left for a person to look at is only what a script cannot. It reads the project's own files and changes nothing. An error is something that
/// breaks the rules, a warning is very likely a mistake, and a review is a heuristic that may be right as written.
/// </summary>
public static partial class ExtensionCheck
{
    private const int MaxFindings = 80;

    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
        { "a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to", "vs", "with" };

    public static CheckResult Run(string folder)
    {
        var findings = new List<CheckFinding>();
        void Add(string severity, string file, int line, string rule, string message)
        {
            if (findings.Count < MaxFindings) findings.Add(new CheckFinding(severity, file.Replace('\\', '/'), line, rule, message));
        }

        string Rel(string path) => Path.GetRelativePath(folder, path);
        bool Skipped(string path) => Rel(path).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj" or "artifacts" or ".vs");

        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => !Skipped(f)).ToList();
        var manifest = CheckManifest(folder, files, Add);
        var toolId = manifest?.Id;

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            var name = Rel(file);
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch (IOException) { continue; }

            switch (extension)
            {
                case ".css": CheckCss(name, lines, Add); break;
                case ".razor": CheckRazor(name, lines, Add); break;
                case ".cs": CheckCode(name, lines, toolId, Add); break;
            }
        }

        // A tool class is what the whole thing hangs on: it must be there, and it must offer its features to the assistant
        var codeFiles = files.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Select(f => (Name: Rel(f), Text: SafeRead(f))).ToList();
        var toolClass = codeFiles.FirstOrDefault(c => c.Text.Contains("IToolModule", StringComparison.Ordinal));
        if (toolClass.Name is null)
            Add("error", "", 0, "tool-class", "No class implements IToolModule, so Seneschal finds no tool in this project");
        else if (!toolClass.Text.Contains("IToolMcp", StringComparison.Ordinal))
            Add("error", toolClass.Name, 1, "every-feature-a-tool", "The tool class does not implement IToolMcp: everything the page lets a person do must also be a tool for the assistant");
        else if (!toolClass.Text.Contains("new McpTool(", StringComparison.Ordinal))
            Add("error", toolClass.Name, 1, "every-feature-a-tool", "IToolMcp is implemented but no McpTool is returned: add a tool for each feature of the page (list, create, run, delete)");

        if (toolClass.Name is not null && !toolClass.Text.Contains("Prompts", StringComparison.Ordinal))
            Add("warn", toolClass.Name, 1, "starter-prompts", "ToolMetadata.Prompts is not set: add one to three short requests, they show as starter cards in the assistant");

        var views = files.Where(f => f.EndsWith("View.razor", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var view in views)
        {
            var text = SafeRead(view);
            var hasEmpty = Regex.IsMatch(text, @"\bNo \w|run-empty|\bempty\b|\.Any\(\)|\.Count\s*(==|>)\s*0", RegexOptions.IgnoreCase);
            var hasWaiting = Regex.IsMatch(text, @"MudSkeleton|MudProgress|_busy|_loading|IsBusy|Loading", RegexOptions.IgnoreCase);
            var hasError = Regex.IsMatch(text, @"MudAlert|Snackbar|catch\b|_error|Error", RegexOptions.IgnoreCase);
            if (!hasEmpty) Add("review", Rel(view), 1, "states", "No empty state found: say what the page shows when there is nothing yet");
            if (!hasWaiting) Add("review", Rel(view), 1, "states", "No loading or busy state found: slow work needs a skeleton, a bar or a disabled button");
            if (!hasError) Add("review", Rel(view), 1, "states", "No error state found: a failure needs a short toast or a MudAlert that says what to do");
        }

        var errors = findings.Count(f => f.Severity == "error");
        var warnings = findings.Count(f => f.Severity == "warn");
        var review = findings.Count(f => f.Severity == "review");
        return new CheckResult(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)), errors, warnings, review, findings);
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return ""; }
    }

    private static ExtensionManifest? CheckManifest(string folder, List<string> files, Action<string, string, int, string, string> add)
    {
        var path = files.FirstOrDefault(f => Path.GetFileName(f).Equals("extension.json", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(f) == folder);
        if (path is null)
        {
            add("error", "extension.json", 0, "manifest", "extension.json is missing");
            return null;
        }

        ExtensionManifest manifest;
        try
        {
            manifest = ExtensionManifest.Parse(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is ExtensionException or JsonException or IOException)
        {
            add("error", "extension.json", 1, "manifest", $"extension.json cannot be read: {ex.Message}");
            return null;
        }

        if (!KebabId().IsMatch(manifest.Id)) add("error", "extension.json", 1, "manifest", $"The id '{manifest.Id}' must be lowercase words joined by hyphens, such as json-diff");
        if (!string.IsNullOrWhiteSpace(manifest.Description) && Regex.IsMatch(manifest.Description.Trim(), @"[.,;!]$|\.\.\.$"))
            add("warn", "extension.json", 1, "wording", "The description ends with punctuation: one short phrase, no period");
        foreach (var style in manifest.Styles)
            if (!files.Any(f => f.Replace('\\', '/').EndsWith("wwwroot/" + style, StringComparison.OrdinalIgnoreCase)))
                add("error", "extension.json", 1, "manifest", $"The stylesheet '{style}' is listed but there is no wwwroot/{style}");
        return manifest;
    }

    private static void CheckCss(string file, string[] lines, Action<string, string, int, string, string> add)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            // What sits inside var(...) is the theme's own value or its fallback, which is how a color is meant to be written
            var line = VarCall().Replace(lines[i], "");
            var number = i + 1;
            if (ColorLiteral().IsMatch(line))
                add("error", file, number, "color", "A color is written here: use a theme variable (--fg, --accent, --border, --card, --bad), so the page follows dark and light mode");
            if (FontSizeLiteral().IsMatch(line))
                add("warn", file, number, "size", "A font size is written here: sizes come from the density scale (--fs-title, --fs-section, --fs-row, --fs-body, --fs-small)");
            if (SpacingLiteral().Match(line) is { Success: true } spacing && OffGrid(spacing.Groups[1].Value))
                add("review", file, number, "grid", $"Spacing of {spacing.Groups[1].Value}px is off the 4px grid: use --gap-1 to --gap-5");
            if (GlobalSelector().IsMatch(line))
                add("error", file, number, "scope", "This rule changes the app or MudBlazor for everyone: name your own classes with a prefix of your own (such as jd-) and style only those");
        }
    }

    private static void CheckRazor(string file, string[] lines, Action<string, string, int, string, string> add)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var number = i + 1;

            if (StyleAttribute().Match(line) is { Success: true } style)
            {
                var value = VarCall().Replace(style.Groups[1].Value, "");
                if (ColorLiteral().IsMatch(value)) add("error", file, number, "color", "A color is written in a style attribute: use a theme variable or a class");
                if (FontSizeLiteral().IsMatch(value)) add("warn", file, number, "size", "A font size is written in a style attribute: sizes come from the density scale");
            }

            foreach (Match button in ButtonText().Matches(line))
            {
                var text = button.Groups[1].Value.Trim();
                if (text.Length == 0) continue;
                if (TrailingPunctuation().IsMatch(text)) add("warn", file, number, "wording", $"The button '{text}' ends with punctuation: none of . , ; ! or ...");
                if (!IsTitleCase(text)) add("warn", file, number, "case", $"The button '{text}' should be in Title Case (Add Container, Get Token)");
            }

            foreach (Match heading in HeadingText().Matches(line))
            {
                var text = heading.Groups[1].Value.Trim();
                if (text.Length > 0 && !IsTitleCase(text)) add("warn", file, number, "case", $"The title '{text}' should be in Title Case");
            }

            foreach (Match plain in PlainText().Matches(line))
            {
                var text = plain.Groups[1].Value.Trim();
                // Prose in the page is short and has no full stop; code samples and URLs are the exceptions a person decides on
                if (text.Length >= 3 && TrailingPunctuation().IsMatch(text) && !text.Contains("://") && !line.Contains("<code", StringComparison.Ordinal))
                    add("review", file, number, "wording", $"'{Shorten(text)}' ends with punctuation: UI text has no trailing . , ; ! or ...");
            }
        }
    }

    private static void CheckCode(string file, string[] lines, string? toolId, Action<string, string, int, string, string> add)
    {
        var prefix = toolId?.Replace('-', '_') + "_";
        var text = string.Join('\n', lines);
        foreach (Match tool in McpToolDeclaration().Matches(text))
        {
            var name = tool.Groups[1].Value;
            var line = text[..tool.Index].Count(c => c == '\n') + 1;
            if (!McpName().IsMatch(name)) add("error", file, line, "mcp-name", $"The tool name '{name}' must be lowercase words joined by underscores, such as {prefix ?? "tool_"}list");
            else if (prefix is not null && !name.StartsWith(prefix, StringComparison.Ordinal))
                add("warn", file, line, "mcp-name", $"The tool name '{name}' should start with the tool's id: {prefix}<verb>");

            // The effect is written a few lines after the name
            var tail = text.Substring(tool.Index, Math.Min(600, text.Length - tool.Index));
            var effect = Regex.Match(tail, @"McpEffect\.(Read|Change|Destructive)");
            if (effect.Success && DestructiveVerb().IsMatch(name) && effect.Groups[1].Value != "Destructive")
                add("error", file, line, "destructive", $"'{name}' removes or overwrites something, so it must be McpEffect.Destructive: that is what makes the assistant ask the user first");
        }

        foreach (Match description in MetadataDescription().Matches(text))
        {
            var value = description.Groups[1].Value.Trim();
            var line = text[..description.Index].Count(c => c == '\n') + 1;
            if (TrailingPunctuation().IsMatch(value)) add("warn", file, line, "wording", $"The description '{Shorten(value)}' ends with punctuation: one short phrase, no period");
        }
    }

    /// <summary>Title Case: every word starts with a capital, except the small words that are not first.</summary>
    private static bool IsTitleCase(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i].Trim('(', ')', '"', '\'', '+', '-');
            if (word.Length == 0 || !char.IsLetter(word[0])) continue;
            if (i > 0 && SmallWords.Contains(word)) continue;
            if (!char.IsUpper(word[0])) return false;
        }
        return true;
    }

    private static bool OffGrid(string value) =>
        int.TryParse(value, out var number) && number > 6 && number % 4 != 0;

    private static string Shorten(string text) => text.Length <= 50 ? text : text[..47] + "...";

    [GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*$")] private static partial Regex KebabId();
    [GeneratedRegex(@"var\([^)]*\)")] private static partial Regex VarCall();
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(")] private static partial Regex ColorLiteral();
    [GeneratedRegex(@"font-size\s*:\s*[\d.]+\s*(px|rem|em|pt)")] private static partial Regex FontSizeLiteral();
    [GeneratedRegex(@"(?:padding|margin|gap)(?:-[a-z]+)?\s*:\s*(\d+)px")] private static partial Regex SpacingLiteral();
    [GeneratedRegex(@"^\s*(?:body|html|\*|\.mud-[\w-]+|\.app-[\w-]+|\.page-[\w-]+)\b[^{]*\{")] private static partial Regex GlobalSelector();
    [GeneratedRegex(@"style=""([^""]*)""")] private static partial Regex StyleAttribute();
    [GeneratedRegex(@"<MudButton\b[^>]*>\s*([^<@{}\n]+?)\s*</MudButton>")] private static partial Regex ButtonText();
    [GeneratedRegex(@"<MudText\b[^>]*Typo=""Typo\.(?:h[1-6])""[^>]*>\s*([^<@{}\n]+?)\s*</MudText>")] private static partial Regex HeadingText();
    [GeneratedRegex(@"<(?:MudText|div|span|p|li)\b[^>]*>\s*([^<>@{}\n]{3,}?)\s*</(?:MudText|div|span|p|li)>")] private static partial Regex PlainText();
    [GeneratedRegex(@"[.,;!]$|\.\.\.$")] private static partial Regex TrailingPunctuation();
    [GeneratedRegex(@"new McpTool\(\s*""([^""]*)""")] private static partial Regex McpToolDeclaration();
    [GeneratedRegex(@"^[a-z][a-z0-9]*(_[a-z0-9]+)+$")] private static partial Regex McpName();
    [GeneratedRegex(@"(^|_)(delete|remove|clear|reset|wipe|drop|overwrite)(_|$)")] private static partial Regex DestructiveVerb();
    [GeneratedRegex(@"Description:\s*""([^""]*)""")] private static partial Regex MetadataDescription();
}
