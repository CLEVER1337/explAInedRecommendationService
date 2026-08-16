using System.Diagnostics;

public sealed class SourceExecutor
{
    private readonly ILogger<SourceExecutor> _logger;

    public SourceExecutor(ILogger<SourceExecutor> logger) => _logger = logger;

    public async Task<(T? Value, SourceOutcome Outcome)> RunAsync<T>(
        string name,
        TimeSpan deadline,
        CancellationToken budget,
        Func<CancellationToken, Task<T>> work)
    {
        if (budget.IsCancellationRequested)
        {
            Record(name, SourceOutcome.Unavailable, TimeSpan.Zero);
            return (default, SourceOutcome.Unavailable);
        }

        using var perSource = CancellationTokenSource.CreateLinkedTokenSource(budget);
        perSource.CancelAfter(deadline);

        var started = Stopwatch.GetTimestamp();

        try
        {
            var task = work(perSource.Token);

            var timeout = Task.Delay(deadline, perSource.Token);
            var winner = await Task.WhenAny(task, timeout);

            if (winner != task)
            {
                Observe(task);
                var outcome = budget.IsCancellationRequested ? SourceOutcome.Unavailable : SourceOutcome.Timeout;
                Record(name, outcome, Stopwatch.GetElapsedTime(started));
                return (default, outcome);
            }

            var value = await task;
            Record(name, SourceOutcome.Ok, Stopwatch.GetElapsedTime(started));
            return (value, SourceOutcome.Ok);
        }
        catch (OperationCanceledException)
        {
            var outcome = budget.IsCancellationRequested ? SourceOutcome.Unavailable : SourceOutcome.Timeout;
            Record(name, outcome, Stopwatch.GetElapsedTime(started));
            return (default, outcome);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feed source {Source} failed", name);
            Record(name, SourceOutcome.Faulted, Stopwatch.GetElapsedTime(started));
            return (default, SourceOutcome.Faulted);
        }
    }

    private static void Observe<T>(Task<T> orphan) =>
        _ = orphan.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

    private static void Record(string name, SourceOutcome outcome, TimeSpan elapsed)
    {
        RecommendationMetrics.SourceLatency.WithLabels(name).Observe(elapsed.TotalSeconds);

        if (outcome != SourceOutcome.Ok)
        {
            RecommendationMetrics.SourceFailures.WithLabels(name, outcome.ToWire()).Inc();
        }
    }
}
