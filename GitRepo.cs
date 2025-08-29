namespace MergeQueuesMonteCarlo;

public class GitRepo
{
    public GitRepo() => _branches["main"] = new GitCommit([], "Initial commit");
    protected readonly Dictionary<string, GitCommit> _branches = new();
    public IReadOnlyDictionary<string, GitCommit> Branches => _branches;

    public int CountBranchesMergedIntoMain
    {
        get
        {
            var main = Branches["main"];
            var i = 0;
            while(main.Parents.Any())
            {
                main = main.Parents.First();
                i++;
            }

            return i;
        }
    }

    private int _branchNumber;

    public (Event, TimeSpan) Merge(string branch)
    {
        var newMainCommit = new GitCommit([_branches["main"], _branches[branch]], $"Merge {branch}");
        _branches["main"] = newMainCommit;
        _branches.Remove(branch);
        return (new BuildTriggeredEvent(newMainCommit, "main"), TimeSpan.Zero);
    }

    public IEnumerable<Event> MakeNewBranch()
    {
        var branch = "branch " + ++_branchNumber;
        var commit = new GitCommit([_branches["main"]], branch);
        _branches[branch] = commit;
        yield return new BuildTriggeredEvent(commit, branch);
    }
}

public class GitRepoWithMergeQueue : GitRepo
{
    private readonly List<(GitCommit, string)> _mergeQueue = new();
    public (Event, TimeSpan) AddToMergeQueue(string branch)
    {
        var mergeCommit = new GitCommit([_mergeQueue.Last().Item1, Branches[branch]], $"Merge {branch}");
        _mergeQueue.Add((mergeCommit, branch));
        _branches[$"queue/{branch}"] = mergeCommit;
        return (new BuildTriggeredEvent(mergeCommit, $"queue/{branch}"), TimeSpan.Zero);
    }
}

public record GitCommit(IEnumerable<GitCommit> Parents, string Message)
{
    public override string ToString() => Message;
}