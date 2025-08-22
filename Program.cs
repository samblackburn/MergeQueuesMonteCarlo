var events = new PriorityQueue<Build, DateTime>();
var repo = new Repo(events);
repo.PushNewBranch(DateTime.Today);
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(3));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(6));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(9));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(12));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(15));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(18));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(21));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(24));
repo.PushNewBranch(DateTime.Today + TimeSpan.FromHours(27));


while (events.Count > 0)
{
    var e = events.Dequeue();
    e.OnFinished();
}

Console.WriteLine($"{Build.NextBuildId} builds were started and {repo.NextBranchId - repo.Branches.Count} branches were merged.");

public class Build(PriorityQueue<Build, DateTime> events, Action<DateTime> onSuccess, Action<DateTime> onFailure)
{
    public static int NextBuildId { get; private set; }
    private static readonly TimeSpan Duration = TimeSpan.FromHours(2);
    private static readonly double SuccessRate = 0.5;

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

public class RenovateBranch(int branchId, PriorityQueue<Build, DateTime> events, HashSet<RenovateBranch> repo)
{
    private Build? _currentBuild; 

    public void Push(DateTime startTime)
    {
        repo.Add(this);
        if (_currentBuild != null) _currentBuild.IsObsolete = true;
        
        Console.WriteLine($"{startTime}: Branch #{branchId} pushed");
        _currentBuild = new Build(events, OnSuccess, _ => { });
        _currentBuild.Start(startTime);
    }

    private void OnSuccess(DateTime time)
    {
        Console.WriteLine($"branch #{branchId} merged and deleted. Rebasing {repo.Count} other branches...");
        repo.Remove(this);
        foreach (var branch in repo)
        {
            branch.Push(time);
        }
    }
}

public class Repo(PriorityQueue<Build, DateTime> events)
{
    public HashSet<RenovateBranch> Branches = new();
    public int NextBranchId { get; private set; }

    public void PushNewBranch(DateTime startTime)
    {
        var renovateBranch = new RenovateBranch(NextBranchId++, events, Branches);
        renovateBranch.Push(startTime);
        Branches.Add(renovateBranch);
    }
}


