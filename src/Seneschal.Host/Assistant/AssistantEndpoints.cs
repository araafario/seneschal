using System.Net;
using Seneschal.Core;

namespace Seneschal;

/// <summary>The two requests the assistant's pictures need: a snip of the window in front, and a picture of a message at full size (for the viewer).</summary>
public static class AssistantEndpoints
{
    public const string SnipPath = "/assistant/snip";

    /// <summary>The header only this app's own page adds. A page of another site cannot add it (that needs a CORS preflight, which is never answered).</summary>
    public const string SnipHeader = "X-Seneschal";

    public static void Map(WebApplication app)
    {
        // A picture of the window the person is using, taken by the app. Only for a request of this machine, from the app's own page
        app.MapPost(SnipPath, (HttpContext http) =>
        {
            if (!IsOwnPage(http) || !http.Request.Headers.ContainsKey(SnipHeader)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var png = WindowCapture.CaptureForeground();
            return png is null ? Results.NoContent() : Results.File(png, "image/png");
        }).DisableAntiforgery();

        // A picture of a message at the size it was sent. Only the messages of this session have it; one opened from the history has only its small copy
        app.MapGet("/assistant/image/{item}/{index:int}", (string item, int index, ClaudeChat chat, HttpContext http) =>
        {
            if (!IsOwnPage(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var image = chat.Items.FirstOrDefault(i => i.Id == item)?.Images.ElementAtOrDefault(index);
            if (image?.Data is not { } data) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(Convert.FromBase64String(data), image.MediaType);
        });
    }

    /// <summary>The request comes from this machine and from a page of this app (the same origin), not from another site.</summary>
    private static bool IsOwnPage(HttpContext http)
    {
        if (!IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None)) return false;
        if (http.Request.Headers.TryGetValue("Origin", out var origin) && origin.ToString() != $"{http.Request.Scheme}://{http.Request.Host}") return false;
        // A picture loaded by the page itself says so; another site's page says cross-site
        return !http.Request.Headers.TryGetValue("Sec-Fetch-Site", out var site) || site.ToString() is "same-origin" or "none";
    }
}
