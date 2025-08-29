namespace MergeQueuesMonteCarlo;

public abstract record Event;
public interface INteresting;
public record BuildTriggeredEvent(GitCommit Commit, string Branch) : Event, INteresting
{
    protected virtual double SuccessProbability => 0.5;

    /// <returns>Uniform distribution between 1 and 3 hours.</returns>
    protected virtual TimeSpan BuildDuration() => TimeSpan.FromHours(1 + 2 * (float)Random.Shared.Next() / int.MaxValue);

    public (Event, TimeSpan) GetCompletionEvent() => (CompletionEvent(), BuildDuration());

    private Event CompletionEvent() => Random.Shared.NextDouble() < SuccessProbability
        ? new BuildSuccessfulEvent(Commit, Branch)
        : new BuildFailedEvent(Commit, Branch);
}

public record ManualRetryBuildEvent(GitCommit Commit, string Branch) : BuildTriggeredEvent(Commit, Branch)
{
    /// <returns>Uniform distribution between 15 and 45 minutes.</returns>
    protected override TimeSpan BuildDuration() => TimeSpan.FromMinutes(15 + 30 * (float)Random.Shared.Next() / int.MaxValue);

    protected override double SuccessProbability => 0.9;
}
public record CheckForFailingBuildsEvent : Event;
internal abstract record BuildCompletedEvent(GitCommit Commit) : Event, INteresting;
internal record BuildSuccessfulEvent(GitCommit Commit, string Branch) : BuildCompletedEvent(Commit);
internal record BuildFailedEvent(GitCommit Commit, string Branch) : BuildCompletedEvent(Commit);
public record RenovateRunEvent : Event;