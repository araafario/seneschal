using Seneschal.Sdk;

namespace Seneschal.Mcp;

/// <param name="Recommended">The assistant's own pick. The person can let Seneschal choose the recommended option for them (Options, Assistant).</param>
public sealed record QuestionOption(string Label, string Description, bool Recommended = false);

/// <summary>What came back for a question: the chosen labels (null when it was skipped or the call ended) and whether Seneschal chose them, not the person.</summary>
public sealed record QuestionAnswer(IReadOnlyList<string>? Labels, bool Automatic);

public sealed class Question(string text, IReadOnlyList<QuestionOption> options, bool multiple, bool allowOther)
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; } = text;
    public IReadOnlyList<QuestionOption> Options { get; } = options;
    public bool Multiple { get; } = multiple;
    public bool AllowOther { get; } = allowOther;

    /// <summary>When Seneschal picks the recommended option by itself, in the timeout mode. Null when the question waits for the person.</summary>
    public DateTimeOffset? AutoAt { get; internal set; }

    /// <summary>The options flagged as recommended (one for a single choice), or the first when none is.</summary>
    public IReadOnlyList<string> RecommendedLabels
    {
        get
        {
            var flagged = Options.Where(o => o.Recommended).Select(o => o.Label).ToList();
            if (flagged.Count == 0) flagged.Add(Options[0].Label);
            return Multiple ? flagged : [flagged[0]];
        }
    }

    internal TaskCompletionSource<QuestionAnswer> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// A question the assistant asks the person (which option, which one of several) waits here until it is answered in the assistant panel, with no time
/// limit, unless the person chose otherwise in Options, Assistant: to let the recommended option be picked at once, or after a wait. Skip, or the call
/// ending, gives the assistant nothing to go on.
/// </summary>
public sealed class McpQuestions(AssistantSettings settings)
{
    private readonly object _gate = new();
    private readonly List<Question> _pending = [];

    public event Action? Changed;

    public IReadOnlyList<Question> Pending
    {
        get { lock (_gate) return [.. _pending]; }
    }

    public async Task<QuestionAnswer> AskAsync(Question question, CancellationToken cancel)
    {
        var mode = settings.QuestionMode;
        if (mode == QuestionModes.Recommended) return new QuestionAnswer(question.RecommendedLabels, true);

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        if (mode == QuestionModes.Timeout)
        {
            var wait = TimeSpan.FromSeconds(settings.QuestionSeconds);
            question.AutoAt = DateTimeOffset.Now + wait;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(wait, waiting.Token);
                    question.Answer.TrySetResult(new QuestionAnswer(question.RecommendedLabels, true));
                }
                catch (OperationCanceledException)
                {
                    // Answered, skipped or ended before the time was up
                }
            });
        }

        lock (_gate) _pending.Add(question);
        Changed?.Invoke();
        try
        {
            await using var _ = cancel.Register(() => question.Answer.TrySetResult(new QuestionAnswer(null, false)));
            return await question.Answer.Task;
        }
        finally
        {
            waiting.Cancel();
            lock (_gate) _pending.Remove(question);
            Changed?.Invoke();
        }
    }

    public void Answer(string id, IReadOnlyList<string>? answers)
    {
        Question? question;
        lock (_gate) question = _pending.FirstOrDefault(q => q.Id == id);
        question?.Answer.TrySetResult(new QuestionAnswer(answers, false));
    }
}