using System.Diagnostics;

var events = new PriorityQueue<Build, DateTime>();
var repo = new Repo(events);
repo.PushNewBranch(DateTime.Today);
repo.PushNewBranch(DateTime.Today + TimeSpan.FromMinutes(3));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromMinutes(6));
var maxParallelBranches = 0;

for(int i=0; i<400; i++)
{
    if (events.Count == 0)
    {
        Console.WriteLine("Pipeline stalled, all builds failed.");
        break;
    }
    maxParallelBranches = Math.Max(maxParallelBranches, repo.Branches.Count);
    var e = events.Dequeue();
    e.OnFinished();
}

Console.WriteLine($"{Build.NextBuildId} builds were started and {repo.NextBranchId - repo.Branches.Count} branches were merged. There were {Build.NextBuildId/(repo.NextBranchId - repo.Branches.Count)} builds per merge.");
Console.WriteLine($"Maximum parallel branches: {maxParallelBranches}");


public class Build(PriorityQueue<Build, DateTime> events, Action<DateTime> onSuccess, Action<DateTime> onFailure)
{
    public static int NextBuildId { get; private set; }
    protected virtual TimeSpan Duration => TimeSpan.FromHours(2);
    protected virtual double SuccessRate => 0.5;

    private readonly int _buildId = ++NextBuildId;
    private DateTime _endTime;
    public bool IsObsolete { get; set; }

    public void Start(DateTime startTime)
    {
        _endTime = startTime + Duration;
        events.Enqueue(this, _endTime);
        Console.WriteLine($"{startTime}: Build #{_buildId} started.");
    }

    public void OnFinished()
    {
        if ((double) Random.Shared.Next() / int.MaxValue >= SuccessRate)
        {
            if (!IsObsolete)
            {
                Console.WriteLine($"{_endTime}: Build #{_buildId} succeeded");
                onSuccess(_endTime);
            }
            else
            {
                Console.WriteLine($"{_endTime}: Build #{_buildId} succeeded but obsolete");
            }
        }
        else
        {
            Console.WriteLine($"{_endTime}: Build #{_buildId} failed");
            onFailure(_endTime);
        }
    }
}

internal class RetryBuild : Build
{
    public RetryBuild(PriorityQueue<Build,DateTime> events, Action<DateTime> onSuccess, Action<DateTime> onFailure) : base(events, onSuccess, onFailure)
    {
    }
    
    protected override TimeSpan Duration => TimeSpan.FromMinutes(30);
    protected override double SuccessRate => 0.9;
}

public class RenovateBranch(int branchId, PriorityQueue<Build, DateTime> events, Repo repo)
{
    private Build? _currentBuild; 

    public void Push(DateTime startTime)
    {
        if (_currentBuild != null) _currentBuild.IsObsolete = true;
        
        Console.WriteLine($"{startTime}: Branch #{branchId} pushed");
        _currentBuild = new Build(events, OnSuccess, OnFailure);
        _currentBuild.Start(startTime);
    }

    private void OnFailure(DateTime obj)
    {
        Debug.Assert(_currentBuild != null, "There must be a build for it to have failed.");
        
        // At 9am the next day, a human hits run on the couple of projects that failed
        var nextMorning = obj.Date + TimeSpan.FromHours(9) + TimeSpan.FromDays(1);
        Console.WriteLine($"{nextMorning}: Manual retry for branch #{branchId}");
        _currentBuild = new RetryBuild(events, OnSuccess, OnFailure);
        _currentBuild.Start(nextMorning);
    }

    private void OnSuccess(DateTime time)
    {
        Console.WriteLine($"branch #{branchId} merged and deleted. Rebasing {repo.Branches.Count} other branches...");
        repo.Branches.Remove(this);
        foreach (var branch in repo.Branches)
        {
            branch.Push(time);
        }
        
        // At this point Renovate will create a new branch from the rate-limited queue
        repo.PushNewBranch(time);
    }
}

public class Repo(PriorityQueue<Build, DateTime> events)
{
    public HashSet<RenovateBranch> Branches = new();
    public int NextBranchId { get; private set; }

    public void PushNewBranch(DateTime startTime)
    {
        var renovateBranch = new RenovateBranch(NextBranchId++, events, this);
        renovateBranch.Push(startTime);
        Branches.Add(renovateBranch);
    }
}