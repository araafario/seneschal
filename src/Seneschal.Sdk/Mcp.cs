using System.Text.Json.Nodes;

namespace Seneschal.Sdk;

/// <summary>
/// What a call to an MCP tool does, which decides how the assistant treats it:
/// <see cref="Read"/> only looks, <see cref="Change"/> changes something that is easy to undo (start, stop, add),
/// <see cref="Destructive"/> removes or overwrites something and always asks the user first.
/// </summary>
public enum McpEffect
{
    Read,
    Change,
    Destructive,
}

/// <summary>The arguments of a call, with the checks every tool needs. A missing or wrong argument throws an <see cref="McpException"/> whose message goes back to the AI.</summary>
public sealed class McpArguments(JsonObject values)
{
    public JsonObject Values { get; } = values;

    public bool Has(string name) => Values[name] is not null;

    public string Text(string name) =>
        Optional(name) ?? throw new McpException($"The argument '{name}' is required");

    public string? Optional(string name) =>
        Values[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    public int Number(string name, int fallback) =>
        Values[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

    public bool Flag(string name, bool fallback = false) =>
        Values[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    public IReadOnlyList<string> List(string name) =>
        Values[name] is JsonArray array
            ? array.Select(item => item?.GetValue<string>()?.Trim() ?? "").Where(s => s.Length > 0).ToList()
            : [];
}

/// <summary>Thrown by a tool with a message the AI can act on ("No project is called Gateway"). The call is reported as failed, not as a crash.</summary>
public sealed class McpException(string message) : Exception(message);

/// <summary>Builds the JSON schema of a tool's arguments without writing JSON by hand.</summary>
public sealed class McpSchema
{
    private readonly JsonObject _properties = [];
    private readonly JsonArray _required = [];

    public static McpSchema None => new();

    public static McpSchema Object() => new();

    public McpSchema Text(string name, string description, bool required = false) => Add(name, "string", description, required);

    public McpSchema Number(string name, string description, bool required = false) => Add(name, "integer", description, required);

    public McpSchema Flag(string name, string description, bool required = false) => Add(name, "boolean", description, required);

    public McpSchema List(string name, string description, bool required = false)
    {
        _properties[name] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>Anything the other methods cannot say, such as a list of objects. The schema is written as JSON.</summary>
    public McpSchema Raw(string name, JsonObject schema, bool required = false)
    {
        _properties[name] = schema.DeepClone();
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>One of a fixed set of values.</summary>
    public McpSchema Choice(string name, string description, IEnumerable<string> values, bool required = false)
    {
        _properties[name] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
        };
        if (required) _required.Add(name);
        return this;
    }

    public JsonObject ToJson()
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = _properties.DeepClone() };
        if (_required.Count > 0) schema["required"] = _required.DeepClone();
        return schema;
    }

    private McpSchema Add(string name, string type, string description, bool required)
    {
        _properties[name] = new JsonObject { ["type"] = type, ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }
}

/// <summary>
/// One thing the AI assistant can do in a tool: a name, a description that tells the AI when to use it, the arguments it takes,
/// and the code to run. <see cref="Run"/> returns anything: text is passed on as it is, other values become JSON.
/// </summary>
public sealed class McpTool(string name, string description, McpEffect effect, McpSchema input, Func<McpArguments, CancellationToken, Task<object?>> run)
{
    /// <summary>Lowercase words joined by underscores and unique across all extensions, such as <c>projects_start</c>. Start with the tool's own name.</summary>
    public string Name { get; } = name;

    public string Description { get; } = description;

    public McpEffect Effect { get; } = effect;

    public McpSchema Input { get; } = input;

    public Func<McpArguments, CancellationToken, Task<object?>> Run { get; } = run;

    /// <summary>A tool that answers at once.</summary>
    public McpTool(string name, string description, McpEffect effect, McpSchema input, Func<McpArguments, object?> run)
        : this(name, description, effect, input, (args, _) => Task.FromResult(run(args)))
    {
    }
}

/// <summary>
/// Optional, implemented by the tool class next to <see cref="IToolModule"/>, to let the AI assistant use what the tool can do:
/// list, create, delete, start, reorder, read. The app finds these by itself and offers them to the assistant while the tool is turned on.
/// The <see cref="IServiceProvider"/> is the app's container, where the services the tool registered live.
/// </summary>
public interface IToolMcp
{
    IEnumerable<McpTool> McpTools(IServiceProvider services);
}
