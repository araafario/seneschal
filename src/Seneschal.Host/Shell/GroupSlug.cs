using System.Text.RegularExpressions;

namespace Seneschal.Shell;

/// <summary>Turns a tool group name such as "Encoders / Decoders" into a URL-safe segment ("encoders-decoders").</summary>
public static partial class GroupSlug
{
    public static string From(string group) => NonAlphanumeric().Replace(group.ToLowerInvariant(), "-").Trim('-');

    public static string Href(string group) => $"/groups/{From(group)}";

    /// <summary>The Favorites page is not a tool group; it lists the starred tools.</summary>
    public const string FavoritesSlug = "favorites";

    public const string FavoritesHref = "/groups/" + FavoritesSlug;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
