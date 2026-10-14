using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Seneschal;

/// <summary>
/// Turns the little Markdown Claude writes (paragraphs, lists, **bold**, `code` and fenced code) into safe HTML.
/// Everything is escaped first, so nothing Claude or a tool returns can inject markup.
/// </summary>
public static partial class ChatMarkup
{
    public static MarkupString Render(string text)
    {
        var html = new StringBuilder();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inCode = false;
        var inList = false;
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append("<p>").Append(string.Join("<br>", paragraph.Select(Inline))).Append("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (!inList) return;
            html.Append("</ul>");
            inList = false;
        }

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                html.Append(inCode ? "</pre>" : "<pre class=\"as-code\">");
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                html.Append(WebUtility.HtmlEncode(line)).Append('\n');
                continue;
            }

            var bullet = Bullet().Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                if (!inList) { html.Append("<ul>"); inList = true; }
                html.Append("<li>").Append(Inline(bullet.Groups[1].Value)).Append("</li>");
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                CloseList();
            }
            else
            {
                CloseList();
                paragraph.Add(line.Trim());
            }
        }

        FlushParagraph();
        CloseList();
        if (inCode) html.Append("</pre>");
        return new MarkupString(html.ToString());
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = Code().Replace(encoded, "<code>$1</code>");
        return Bold().Replace(encoded, "<b>$1</b>");
    }

    [GeneratedRegex(@"^\s*(?:[-*]|\d+\.)\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex Bold();
}
