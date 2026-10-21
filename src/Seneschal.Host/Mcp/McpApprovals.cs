using System.Text.Json.Nodes;
using Seneschal.Sdk;

namespace Seneschal.Mcp;

public sealed class ApprovalRequest(string tool, string source, string description, string arguments, bool destructive)
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Tool { get; } = tool;
    public string Source { get; } = source;
    public string Description { get; } = description;
    public string Arguments { get; } = arguments;
    public bool Destructive { get; } = destructive;
    public DateTime Asked { get; } = DateTime.Now;
    internal TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// A call the AI makes that deletes something (or any change, when the user chose to be asked) waits here until the user answers
/// on the assistant page. It waits with no time limit: the person allows it, denies it, or chose to allow every action (Options, Assistant). The call
/// ending (the client went away, or the run was stopped) counts as no.
/// </summary>
public sealed class McpApprovals(AssistantSettings settings)
{

    private readonly object _gate = new();
    private readonly List<ApprovalRequest> _pending = [];

    public event Action? Changed;

    public IReadOnlyList<ApprovalRequest> Pending
    {
        get { lock (_gate) return [.. _pending]; }
    }

    public bool Needs(McpEffect effect) =>
        !settings.AlwaysAllow && (effect == McpEffect.Destructive || (effect == McpEffect.Change && settings.AskBeforeChanges));

    public async Task<bool> AskAsync(string tool, string source, string description, JsonObject arguments, bool destructive, CancellationToken cancel)
    {
        var request = new ApprovalRequest(tool, source, description, arguments.ToJsonString(new() { WriteIndented = true }), destructive);
        lock (_gate) _pending.Add(request);
        Changed?.Invoke();
        try
        {
            await using var _ = cancel.Register(() => request.Answer.TrySetResult(false));
            return await request.Answer.Task;
        }
        finally
        {
            lock (_gate) _pending.Remove(request);
            Changed?.Invoke();
        }
    }

    public void Answer(string id, bool allow)
    {
        ApprovalRequest? request;
        lock (_gate) request = _pending.FirstOrDefault(r => r.Id == id);
        request?.Answer.TrySetResult(allow);
    }
}
