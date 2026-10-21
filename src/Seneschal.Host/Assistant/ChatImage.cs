namespace Seneschal;

/// <summary>An image as the browser hands it over: already scaled down, as base64, with a small copy to show in the chat and the history.</summary>
public sealed record ChatImageIn(string MediaType, string Data, string Thumb);

/// <summary>An image in a message. <see cref="Data"/> is the picture itself (base64) and is only there for the message that was just sent: a chat opened from the history keeps the small <see cref="Thumb"/> (a data URL) to show.</summary>
public sealed record ChatImage(string MediaType, string? Data, string Thumb)
{
    public const int MaxImages = 4;

    /// <summary>The most a single image may weigh as base64 (about 3 MB of picture). The browser scales them down well below this.</summary>
    public const int MaxBase64 = 4_000_000;

    private static readonly string[] Types = ["image/png", "image/jpeg", "image/webp", "image/gif"];

    /// <summary>The images that may be sent: a known type, base64 that decodes, not too many, not too large. Anything else is left out.</summary>
    public static List<ChatImage> From(IEnumerable<ChatImageIn>? incoming)
    {
        var result = new List<ChatImage>();
        foreach (var image in incoming ?? [])
        {
            if (result.Count >= MaxImages) break;
            if (!Types.Contains(image.MediaType) || string.IsNullOrEmpty(image.Data) || image.Data.Length > MaxBase64) continue;
            if (!Thumbnail(image.Thumb)) continue;
            try { _ = Convert.FromBase64String(image.Data); }
            catch (FormatException) { continue; }
            result.Add(new ChatImage(image.MediaType, image.Data, image.Thumb));
        }
        return result;
    }

    /// <summary>The small copy must be a data URL of an image (it is put in a page), and small.</summary>
    private static bool Thumbnail(string? thumb) =>
        thumb is { Length: > 0 and < 200_000 } && (thumb.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) || thumb.StartsWith("data:image/png;base64,", StringComparison.Ordinal));
}
