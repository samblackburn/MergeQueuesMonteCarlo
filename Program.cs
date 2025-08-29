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

float unmergedBranches = repo.Branches.Count;
float mergedBranches = repo.NextBranchId - unmergedBranches;
var builds = BuildStartedEvent.NextBuildId;
Console.WriteLine($"{builds} builds were started and {mergedBranches} branches were merged. ");
Console.WriteLine($"There were {builds/mergedBranches} branch-or-merge-queue builds per merge.");
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
            Console.WriteLine($"{EndTime}: Build #{_buildId} succeeded");
            onSuccess(EndTime);
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

public class BranchWithMergeQueuesEnabled(int branchId, PriorityQueue<IEvent, DateTime> events, MergeQueue queue)
{
    private BuildStartedEvent? _currentBuild;
    public int BranchId => branchId;

    public void Push(DateTime startTime)
    {
        _currentBuild = new BuildStartedEvent(events, OnSuccess, OnFailure);
        _currentBuild.Start(startTime, $"Branch #{branchId} pushed");
    }

    private void OnFailure(DateTime obj)
    {
    }

    private void OnSuccess(DateTime time)
    {
        Console.WriteLine($"branch #{branchId} added to merge queue");
        queue.Add(this, time);
    }

    public void ManualRetry(DateTime checkTime)
    {
        Debug.Assert(_currentBuild != null, "There should be a build to retry");
        
        if (_currentBuild.EndTime > checkTime)
        {
            // Build is still running
            return;
        }
        
        _currentBuild = new BuildStartedEvent(events, OnSuccess, OnFailure);
        _currentBuild.Start(checkTime, $"Manually retrying branch #{branchId}");
    }
}

public class MergeQueue(PriorityQueue<IEvent, DateTime> events)
{
    // TODO minimum number of PRs in queue, with backoff time
    
    public List<BranchWithMergeQueuesEnabled> Queue = new();
    public Dictionary<BranchWithMergeQueuesEnabled, BuildStatus> Statuses = new();
    public Repo Repo { get; set; }

    public void Add(BranchWithMergeQueuesEnabled pr, DateTime time)
    {
        Queue.Add(pr);
        Statuses.Add(pr, BuildStatus.Pending);
        
        events.Enqueue(new MergeQueueBuildStartedEvent(events, _ => OnSuccess(pr), finishTime => RemoveBranchFromQueue(pr, finishTime)), time);
    }

    private void RemoveBranchFromQueue(BranchWithMergeQueuesEnabled pr, DateTime time)
    {
        var prWasHead = Queue.Last() == pr;
        Queue.Remove(pr);
        Statuses.Remove(pr);
        if (!Queue.Any()) return;
        if (prWasHead)
        {
            // Head of queue was removed, Queue.Last() is new head of queue
            // If build is running, do nothing
            if (Statuses[Queue.Last()] == BuildStatus.Pending) return;
            // If build failed, we shouldn't be here because OnFailure would have removed it from the queue
            Debug.Assert(Statuses[Queue.Last()] == BuildStatus.Passed, "The head of the queue should have a successful build");
            // If build succeeded, merge the rest of the queue
            OnSuccess(Queue.Last());
        }
        else
        {
            // Rebase head of queue, triggering a new build
            events.Enqueue(new MergeQueueBuildStartedEvent(events, _ => OnSuccess(Queue.Last()), finishTime => RemoveBranchFromQueue(pr, finishTime)), time);
        }
    }

    private void OnSuccess(BranchWithMergeQueuesEnabled pr)
    {
        if (pr != Queue.Last())
        {
            Console.WriteLine($"Merge queue build for branch #{pr.BranchId} is obsolete. Maybe if the head of the queue fails we'll merge anyway?");
            Statuses[pr] = BuildStatus.Passed;
            return;
        }

        foreach (var branch in Queue)
        {
            Console.WriteLine($"Merging branch #{branch.BranchId}");
            Statuses.Remove(branch);
            Repo.Branches.Remove(branch);
        }

        Queue.Clear();
        // No branches get rebased as a result of a successful merge
    }
}

public enum BuildStatus
{
    Pending,
    Passed,
    Failed
}

public class MergeQueueBuildStartedEvent(PriorityQueue<IEvent, DateTime> events, Action<DateTime> onSuccess, Action<DateTime> onFailure) : BuildStartedEvent(events, onSuccess, onFailure)
{
}

public class Repo(PriorityQueue<IEvent, DateTime> events)
{
    public HashSet<BranchWithMergeQueuesEnabled> Branches = new();
    public MergeQueue MergeQueue = new(events);
    public int NextBranchId { get; private set; }

    public void PushNewBranch(DateTime startTime)
    {
        MergeQueue.Repo = this;
        var renovateBranch = new BranchWithMergeQueuesEnabled(++NextBranchId, events, MergeQueue);
        renovateBranch.Push(startTime);
        Branches.Add(renovateBranch);
    }
}

public interface IEvent
{
    public void OnFinished();
}