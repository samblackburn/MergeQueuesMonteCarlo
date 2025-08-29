using System.Diagnostics;

var events = new PriorityQueue<IEvent, DateTime>();
var repo = new Repo(events);
repo.PushNewBranch(DateTime.Today);
repo.PushNewBranch(DateTime.Today + TimeSpan.FromMinutes(3));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromMinutes(6));
var firstTimeToManuallyCheckBuilds = DateTime.Today + TimeSpan.FromHours(9);
events.Enqueue(new RetryFailingBuildsEvent(events, firstTimeToManuallyCheckBuilds, repo), firstTimeToManuallyCheckBuilds);
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

Console.WriteLine($"{BuildStartedEvent.NextBuildId} builds were started and {repo.NextBranchId - repo.Branches.Count} branches were merged. ");
Console.WriteLine($"There were {BuildStartedEvent.NextBuildId/((float)repo.NextBranchId - repo.Branches.Count)} branch builds per merge.");
Console.WriteLine($"Maximum parallel branches: {maxParallelBranches}");


public class BuildStartedEvent(PriorityQueue<IEvent, DateTime> events, Action<DateTime> onSuccess, Action<DateTime> onFailure) : IEvent
{
    public static int NextBuildId { get; private set; }
    
    /// <summary>
    /// Uniform distribution between 1 and 3 hours
    /// </summary>
    protected virtual TimeSpan Duration => TimeSpan.FromHours(1) + TimeSpan.FromHours(2) * Random.Shared.NextDouble();
    protected virtual double SuccessRate => 0.5;

    private readonly int _buildId = ++NextBuildId;
    public DateTime EndTime { get; private set; }
    public bool IsObsolete { get; set; }

    public void Start(DateTime startTime, string message)
    {
        EndTime = startTime + Duration;
        events.Enqueue(this, EndTime);
        Console.WriteLine($"{startTime}: {message}; build #{_buildId} started.");
    }

    public void OnFinished()
    {
        if ((double) Random.Shared.Next() / int.MaxValue >= SuccessRate)
        {
            if (!IsObsolete)
            {
                Console.WriteLine($"{EndTime}: Build #{_buildId} succeeded");
                onSuccess(EndTime);
            }
            else
            {
                Console.WriteLine($"{EndTime}: Build #{_buildId} succeeded but obsolete");
            }
        }
        else
        {
            Console.WriteLine($"{EndTime}: Build #{_buildId} failed");
            onFailure(EndTime);
        }
    }
}

internal class RetryFailingBuildsEvent(PriorityQueue<IEvent, DateTime> events, DateTime checkTime, Repo repo) : IEvent
{
    public void OnFinished()
    {
        var nextTimeToCheck = checkTime + TimeSpan.FromDays(1);
        events.Enqueue(new RetryFailingBuildsEvent(events, nextTimeToCheck, repo), nextTimeToCheck);
        RetryAllFailingBranches();
    }

    private void RetryAllFailingBranches()
    {
        foreach (var branch in repo.Branches)
        {
            branch.ManualRetry(checkTime);
        }
    }
}

public class RenovateBranch(int branchId, PriorityQueue<IEvent, DateTime> events, Repo repo)
{
    private BuildStartedEvent? _currentBuild; 

    public void Push(DateTime startTime)
    {
        if (_currentBuild != null) _currentBuild.IsObsolete = true;
        
        _currentBuild = new BuildStartedEvent(events, OnSuccess, OnFailure);
        _currentBuild.Start(startTime, $"Branch #{branchId} pushed");
    }

    private void OnFailure(DateTime obj)
    {
    }

    private void OnSuccess(DateTime time)
    {
        repo.Branches.Remove(this);
        Console.WriteLine($"branch #{branchId} merged and deleted. Rebasing {repo.Branches.Count} other branches...");
        foreach (var branch in repo.Branches)
        {
            branch.Push(time);
        }

        Console.WriteLine($"{time}: Renovate creates a new branch from the rate-limited queue");
        repo.PushNewBranch(time);
    }

    public void ManualRetry(DateTime checkTime)
    {
        Debug.Assert(_currentBuild != null, "There should be a build to retry");
        Debug.Assert(!_currentBuild.IsObsolete, "The build should not be obsolete");
        
        if (_currentBuild.EndTime > checkTime)
        {
            // Build is still running
            return;
        }
        
        _currentBuild = new BuildStartedEvent(events, OnSuccess, OnFailure);
        _currentBuild.Start(checkTime, $"Manually retrying branch #{branchId}");
    }
}

public class Repo(PriorityQueue<IEvent, DateTime> events)
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

public interface IEvent
{
    public void OnFinished();
}