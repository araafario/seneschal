namespace Seneschal.Mcp;

public sealed record QuestionOption(string Label, string Description);

public sealed class Question(string text, IReadOnlyList<QuestionOption> options, bool multiple, bool allowOther)
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; } = text;
    public IReadOnlyList<QuestionOption> Options { get; } = options;
    public bool Multiple { get; } = multiple;
    public bool AllowOther { get; } = allowOther;
    internal TaskCompletionSource<IReadOnlyList<string>?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// A question the assistant asks the person (which option, which one of several) waits here until it is answered in the assistant panel.
/// The answer is the chosen labels, or the person's own words. No answer within a few minutes, or Skip, gives the assistant nothing to go on.
/// </summary>
public sealed class McpQuestions
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly List<Question> _pending = [];

    public event Action? Changed;

    public IReadOnlyList<Question> Pending
    {
        get { lock (_gate) return [.. _pending]; }
    }

    public async Task<IReadOnlyList<string>?> AskAsync(Question question, CancellationToken cancel)
    {
        lock (_gate) _pending.Add(question);
        Changed?.Invoke();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(Patience);
            await using var _ = timeout.Token.Register(() => question.Answer.TrySetResult(null));
            return await question.Answer.Task;
        }
        finally
        {
            lock (_gate) _pending.Remove(question);
            Changed?.Invoke();
        }
    }

    public void Answer(string id, IReadOnlyList<string>? answers)
    {
        Question? question;
        lock (_gate) question = _pending.FirstOrDefault(q => q.Id == id);
        question?.Answer.TrySetResult(answers);
    }
}
