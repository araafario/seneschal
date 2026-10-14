using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Seneschal.Sdk;

namespace Seneschal.Mcp;

/// <summary>
/// The MCP server of the app (Streamable HTTP, JSON answers): <c>initialize</c>, <c>tools/list</c> and <c>tools/call</c> over JSON-RPC.
/// Everything that is on in the app and implements <see cref="IToolMcp"/> is offered. Only this machine can reach it, and only with the
/// secret in <see cref="AssistantSettings"/>.
/// </summary>
public sealed class McpServer(McpRegistry registry, McpApprovals approvals, AssistantSettings settings, ILogger<McpServer> log)
{
    public const string Path = "/mcp";
    private const string Fallback = "2025-06-18";

    private static readonly JsonSerializerOptions Output = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    /// <summary>A tool failed or was refused. The text goes back to the AI, which tells the user.</summary>
    private sealed class Failure(string message) : Exception(message);

    public bool IsAllowed(HttpContext http)
    {
        var host = http.Request.Host.Host;
        if (!(host is "localhost" or "127.0.0.1" or "[::1]" or "::1")) return false;
        // A web page in a browser always sends an Origin; Claude does not
        if (http.Request.Headers.ContainsKey("Origin")) return false;

        var header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()), Encoding.UTF8.GetBytes(settings.Secret));
    }

    /// <summary>Answers one JSON-RPC message. Null means there is nothing to answer (a notification).</summary>
    public async Task<JsonNode?> HandleAsync(JsonNode message, CancellationToken cancel)
    {
        var id = message["id"]?.DeepClone();
        var method = message["method"]?.GetValue<string>();
        if (method is null) return null;
        if (id is null) return null; // notifications such as notifications/initialized

        try
        {
            JsonNode result = method switch
            {
                "initialize" => Initialize(message["params"]),
                "ping" => new JsonObject(),
                "tools/list" => ListTools(),
                "tools/call" => await CallAsync(message["params"] as JsonObject, cancel),
                _ => throw new JsonRpcError(-32601, $"Method not found: {method}"),
            };
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }
        catch (JsonRpcError error)
        {
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = error.Code, ["message"] = error.Message } };
        }
    }

    private sealed class JsonRpcError(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private static JsonObject Initialize(JsonNode? parameters) => new()
    {
        ["protocolVersion"] = parameters?["protocolVersion"]?.GetValue<string>() ?? Fallback,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
        ["serverInfo"] = new JsonObject { ["name"] = "seneschal", ["version"] = SdkInfo.Version.ToString(3) },
        ["instructions"] = "Seneschal is the user's local developer toolbelt. These tools do what the user can do in its pages. Prefer a list tool before acting on a name, and say what you did in a short sentence.",
    };

    private JsonObject ListTools() => new()
    {
        ["tools"] = new JsonArray(registry.All().Select(entry => (JsonNode)new JsonObject
        {
            ["name"] = entry.Tool.Name,
            ["description"] = entry.Tool.Description,
            ["inputSchema"] = entry.Tool.Input.ToJson(),
            ["annotations"] = new JsonObject
            {
                ["readOnlyHint"] = entry.Tool.Effect == McpEffect.Read,
                ["destructiveHint"] = entry.Tool.Effect == McpEffect.Destructive,
            },
        }).ToArray()),
    };

    private async Task<JsonObject> CallAsync(JsonObject? parameters, CancellationToken cancel)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? throw new JsonRpcError(-32602, "A tool name is required");
        var entry = registry.Find(name) ?? throw new JsonRpcError(-32602, $"Unknown tool: {name}");
        var arguments = parameters["arguments"] as JsonObject ?? [];

        try
        {
            if (approvals.Needs(entry.Tool.Effect))
            {
                var allowed = await approvals.AskAsync(entry.Tool.Name, entry.Source, entry.Tool.Description, arguments, entry.Tool.Effect == McpEffect.Destructive, cancel);
                if (!allowed) throw new Failure("The user did not allow this, so nothing was changed");
            }

            var result = await entry.Tool.Run(new McpArguments(arguments), cancel);
            return Reply(Render(result), isError: false);
        }
        catch (McpException ex)
        {
            return Reply(ex.Message, isError: true);
        }
        catch (Failure ex)
        {
            return Reply(ex.Message, isError: true);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "MCP tool {Tool} failed", name);
            return Reply($"{name} failed: {ex.Message}", isError: true);
        }
    }

    private static string Render(object? result) => result switch
    {
        null => "Done",
        string text => text,
        _ => JsonSerializer.Serialize(result, Output),
    };

    private static JsonObject Reply(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };
}

/// <summary>Where Claude can reach the MCP endpoint: the first plain-http address the app listens on (Claude's own certificate store does not know the dev certificate).</summary>
public sealed class McpAddress(IServiceProvider services)
{
    public string? Url
    {
        get
        {
            var addresses = services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
            var http = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
            if (http is null) return null;
            var uri = new Uri(http.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost"));
            return $"{uri.Scheme}://localhost:{uri.Port}{McpServer.Path}";
        }
    }
}
