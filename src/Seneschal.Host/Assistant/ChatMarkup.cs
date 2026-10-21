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
        string? list = null; // "ul" or "ol" while a list is open
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append("<p>").Append(string.Join("<br>", paragraph.Select(Inline))).Append("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (list is null) return;
            html.Append("</").Append(list).Append('>');
            list = null;
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

            var heading = Heading().Match(line);
            var bullet = Bullet().Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                CloseList();
                html.Append("<p class=\"as-h\">").Append(Inline(heading.Groups[1].Value)).Append("</p>");
            }
            else if (bullet.Success)
            {
                FlushParagraph();
                var kind = bullet.Groups[1].Success ? "ol" : "ul";
                if (list != kind)
                {
                    CloseList();
                    // A numbered list keeps the number it starts with
                    html.Append('<').Append(kind);
                    if (kind == "ol" && bullet.Groups[1].Value != "1") html.Append(" start=\"").Append(bullet.Groups[1].Value).Append('"');
                    html.Append('>');
                    list = kind;
                }
                html.Append("<li>").Append(Inline(bullet.Groups[2].Value)).Append("</li>");
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
        encoded = Link().Replace(encoded, "<a href=\"$2\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>");
        return Bold().Replace(encoded, "<b>$1</b>");
    }

    // A numbered item (group 1 is its number) or a bullet; group 2 is the text
    [GeneratedRegex(@"^\s*(?:(\d{1,3})[.)]|[-*])\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s{0,3}#{1,4}\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    // Only web links. The text was HTML-encoded first, so a quote in the address cannot end the attribute
    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex Bold();
}
