namespace MergeQueuesMonteCarlo;

public interface IProcessor
{
    IEnumerable<(Event, TimeSpan)> HandleEvent(Event e);
}

public class BuildStartProcessor : IProcessor
{
    public IEnumerable<(Event, TimeSpan)> HandleEvent(Event e)
    {
        if (e is not BuildTriggeredEvent bte) yield break;
        yield return bte.GetCompletionEvent();
    }
}

public class BuildCompletedProcessor(Dictionary<GitCommit, BuildStatus> buildStatus) : IProcessor
{
    public IEnumerable<(Event, TimeSpan)> HandleEvent(Event e)
    {
        if (e is not BuildCompletedEvent bce) yield break;
        buildStatus[bce.Commit] = bce switch
        {
            BuildSuccessfulEvent => BuildStatus.Success,
            BuildFailedEvent => BuildStatus.Failure
        };
    }
}

public class MergeWhenGreenProcessor(GitRepo repo) : IProcessor
{
    public IEnumerable<(Event, TimeSpan)> HandleEvent(Event e)
    {
        if (e is not BuildSuccessfulEvent bse) yield break;
        if (bse.Commit != repo.Branches[bse.Branch] || bse.Branch == "main") yield break;
        yield return repo.Merge(bse.Branch);
    }
}

public class AddToMergeQueueWhenGreenProcessor(GitRepoWithMergeQueue repo) : IProcessor
{
    public IEnumerable<(Event, TimeSpan)> HandleEvent(Event e)
    {
        if (e is not BuildSuccessfulEvent bse) yield break;
        if (bse.Commit != repo.Branches[bse.Branch] || bse.Branch == "main" || bse.Branch.StartsWith("queue")) yield break;
        yield return repo.AddToMergeQueue(bse.Branch);
    }
}

public class RenovatePrGenerationProcessor(GitRepo repo) : IProcessor
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    
    public IEnumerable<(Event, TimeSpan)> HandleEvent(Event e)
    {
        if (e is not RenovateRunEvent) yield break;
        yield return (new RenovateRunEvent(), Interval);
        while (repo.Branches.Count(b => b.Key != "main") < 3)
        {
            foreach (var evt in repo.MakeNewBranch())
            {
                yield return (evt, TimeSpan.Zero);
            }
        }
    }
}

public class ManualRetryBuildProcessor(GitRepo repo, Dictionary<GitCommit, BuildStatus> buildStatus) : IProcessor
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public IEnumerable<(Event, TimeSpan)> HandleEvent(Event e)
    {
        if (e is not CheckForFailingBuildsEvent) yield break;
        yield return (new CheckForFailingBuildsEvent(), Interval);
        foreach (var branch in repo.Branches)
        {
            if (branch.Key == "main") continue;
            if (!buildStatus.TryGetValue(branch.Value, out var status) || status != BuildStatus.Failure) continue;
            yield return (new ManualRetryBuildEvent(repo.Branches[branch.Key], branch.Key), TimeSpan.Zero);
            buildStatus[branch.Value] = BuildStatus.Retrying;
        }
    }
}