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

    public virtual IEnumerable<(Event, TimeSpan)> Merge(string branch)
    {
        var newMainCommit = new GitCommit([_branches["main"], _branches[branch]], $"Merge {branch}");
        _branches["main"] = newMainCommit;
        _branches.Remove(branch);
        yield return (new BuildTriggeredEvent(newMainCommit, "main"), TimeSpan.Zero);
    }

    public IEnumerable<Event> MakeNewBranch()
    {
        var branch = "branch " + ++_branchNumber;
        var commit = new GitCommit([_branches["main"]], branch);
        _branches[branch] = commit;
        yield return new BuildTriggeredEvent(commit, branch);
    }

    public (Event, TimeSpan) RebaseBranch(string branch)
    {
        var commit = new GitCommit([_branches["main"], _branches[branch]], $"Merge main into {branch}");
        _branches[branch] = commit;
        return (new BuildTriggeredEvent(commit, branch), TimeSpan.Zero);
    }
}

public class GitRepoWithMergeQueue(IReadOnlyDictionary<GitCommit, BuildStatus> statuses) : GitRepo
{
    private readonly List<(GitCommit, string)> _mergeQueue = new();
    public IEnumerable<(Event, TimeSpan)> AddToMergeQueue(string branch)
    {
        if (_mergeQueue.Any(q => q.Item2 == branch)) yield break;
        var mergeCommit = _mergeQueue.Any()
            ? new GitCommit([_mergeQueue.Last().Item1, Branches[branch]], $"Merge {branch}")
            : Branches[branch];
        _mergeQueue.Add((mergeCommit, branch));
        _branches[$"queue/{branch}"] = mergeCommit;
        Console.WriteLine($"    {branch} added to merge queue");
        yield return (new BuildTriggeredEvent(mergeCommit, $"queue/{branch}"), TimeSpan.Zero);
    }

    public override IEnumerable<(Event, TimeSpan)> Merge(string branch)
    {
        if (!branch.StartsWith("queue/")) throw new Exception("Can only merge via queues");
        
        var headOfQueue = _mergeQueue.LastOrDefault();
        if (headOfQueue == default) throw new Exception("Merge queue build succeeded but queue somehow empty");
        if (headOfQueue.Item1 != Branches[branch]) yield break;
        
        Console.WriteLine($"    Pushing queue with {_mergeQueue.Count} items to main");
        _branches["main"] = headOfQueue.Item1;
        _mergeQueue.Clear();
        yield return (new BuildTriggeredEvent(headOfQueue.Item1, "main"), TimeSpan.Zero);
    }

    public IEnumerable<(Event, TimeSpan)> Reject(GitCommit commit)
    {
        var failingHead = _mergeQueue.LastOrDefault();
        if (failingHead.Item1 != commit) return [];
        _mergeQueue.Remove(failingHead);

        var headOfRemainingQueue = _mergeQueue.LastOrDefault();
        if (statuses.TryGetValue(headOfRemainingQueue.Item1, out var status) && status == BuildStatus.Success)
        {
            Console.WriteLine($"    {failingHead.Item2} failed but {headOfRemainingQueue.Item2} is green, pushing to main");
            return Merge(headOfRemainingQueue.Item2);
        }

        return [];
    }
}

public record GitCommit(IEnumerable<GitCommit> Parents, string Message)
{
    private readonly HashSet<GitCommit> _allAncestors = [..Parents.SelectMany(p => p._allAncestors.Append(p))]; 
    public override string ToString() => Message;
    public bool HasAncestor(GitCommit needle) => _allAncestors.Contains(needle);
}