using Seneschal;
using Seneschal.Core;
using Seneschal.Mcp;
using Seneschal.Shell;
using Microsoft.AspNetCore.StaticFiles;
using MudBlazor.Services;

// "seneschal stop", "seneschal open" and the like are handled here and never start the web app
if (Seneschal.Cli.EngineCli.Handle(args) is { } exitCode) return exitCode;
Seneschal.Cli.EngineMode.Prepare();

var builder = WebApplication.CreateBuilder(args);

// The stylesheets and scripts of the app and of MudBlazor are found through a manifest that a build (not a publish) leaves next to the exe.
// Only the Development environment reads it by itself: without this, an exe run from a build folder serves empty files and the page has no styles
builder.WebHost.UseStaticWebAssets();

// An open browser tab holds a connection that would otherwise keep the app from ending for 30 seconds after Quit or Restart
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices(config =>
{
    // Toasts: bottom right, gone after a few seconds, with a close button. An error asks to be dismissed (see the pages).
    var toast = config.SnackbarConfiguration;
    toast.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
    toast.ShowCloseIcon = true;
    toast.VisibleStateDuration = 3500;
    toast.MaxDisplayedSnackbars = 4;
});
builder.Services.AddExtensionManagement();
builder.Services.AddExtensions(builder.Environment);   // loads the extensions now, so they can register their own services before the container is built
builder.Services.AddTools();
builder.Services.AddSingleton<UiSettings>();
builder.Services.AddSingleton<GroupIcons>();
builder.Services.AddSingleton<Favorites>();
builder.Services.AddSingleton<AssistantSettings>();
builder.Services.AddSingleton<HostMcpTools>();
builder.Services.AddSingleton<McpRegistry>();
builder.Services.AddSingleton<McpApprovals>();
builder.Services.AddSingleton<McpServer>();
builder.Services.AddSingleton<McpAddress>();
builder.Services.AddSingleton<ChatHistory>();
builder.Services.AddSingleton<Seneschal.Updates.UpdateState>();
builder.Services.AddHostedService<Seneschal.Updates.UpdateService>();
builder.Services.AddSingleton<ClaudeChat>();

var app = builder.Build();
app.Services.GetRequiredService<DllExtensions>().LoadAll();
app.Services.StartTools();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// The MCP endpoint is used by Claude on this machine over plain http, so it is left out of the redirect to https.
// An app that only listens on http (the installed one) has nothing to redirect to.
if ((builder.Configuration["urls"] ?? "").Contains("https://", StringComparison.OrdinalIgnoreCase))
    app.UseWhen(http => !http.Request.Path.StartsWithSegments(McpServer.Path), branch => branch.UseHttpsRedirection());

app.UseAntiforgery();

app.MapStaticAssets();

// The static files of the extensions (their stylesheets), from each extension's own wwwroot
var contentTypes = new FileExtensionContentTypeProvider();
app.MapGet("/_ext/{id}/{**path}", (string id, string path, ExtensionAssets assets, HttpContext http) =>
{
    if (assets.Resolve(id, path) is not { } file) return Results.NotFound();
    http.Response.Headers.CacheControl = "no-cache"; // the ?v= in the link changes with the version; a dev build is checked every time
    return Results.File(file, contentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream");
});

// MCP over HTTP: one JSON-RPC message per POST, answered with JSON
app.MapPost(McpServer.Path, async (HttpContext http, McpServer mcp) =>
{
    if (!mcp.IsAllowed(http)) return Results.Unauthorized();
    System.Text.Json.Nodes.JsonNode? message;
    try { message = await System.Text.Json.Nodes.JsonNode.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(); }
    if (message is not System.Text.Json.Nodes.JsonObject) return Results.BadRequest();
    var answer = await mcp.HandleAsync(message, http.RequestAborted);
    return answer is null ? Results.Accepted() : Results.Json(answer);
}).DisableAntiforgery();
app.MapGet(McpServer.Path, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

Seneschal.Cli.EngineMode.Map(app);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;
