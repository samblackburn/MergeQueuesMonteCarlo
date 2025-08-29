using MergeQueuesMonteCarlo;

var mergeQueues = true;

var repo = new GitRepoWithMergeQueue();
var buildStatus = new Dictionary<GitCommit, BuildStatus>();

var eventQueue = new PriorityQueue<Event, DateTime>();
eventQueue.Enqueue(new RenovateRunEvent(), DateTime.Today);
eventQueue.Enqueue(new CheckForFailingBuildsEvent(), DateTime.Today.AddHours(9));

var history = new List<(DateTime time, Event evt)>();

var processors = mergeQueues
    ? new List<IProcessor>
    {
        new BuildStartProcessor(),
        new BuildCompletedProcessor(buildStatus),
        new AddToMergeQueueWhenGreenProcessor(repo),
        new RenovatePrGenerationProcessor(repo),
        new ManualRetryBuildProcessor(repo, buildStatus)
    }
    : new List<IProcessor>
    {
        new BuildStartProcessor(),
        new BuildCompletedProcessor(buildStatus),
        new MergeWhenGreenProcessor(repo),
        new RenovatePrGenerationProcessor(repo),
        new ManualRetryBuildProcessor(repo, buildStatus)
    };

var weekdays = TimeSpan.FromDays(5);
while (eventQueue.TryDequeue(out var eventItem, out var time) && time < DateTime.Today + weekdays)
{
    history.Add((time, eventItem));
    if (eventItem is INteresting) Console.WriteLine($"{time}: {eventItem}");
    eventQueue.EnqueueRange(processors.SelectMany(p => p.HandleEvent(eventItem).ToAbsolute(time)));
}

Console.WriteLine($"{repo.CountBranchesMergedIntoMain} branches merged into main over {weekdays.TotalDays} days.");
var mainBuilds = history.Count(x => x.evt is BuildTriggeredEvent bte && bte.Branch == "main");
var branchBuilds = history.Count(x => x.evt is BuildTriggeredEvent bse && bse.Branch != "main");
var manualRetries = history.Count(x => x.evt is ManualRetryBuildEvent);
Console.WriteLine($"There were {mainBuilds} main builds and {branchBuilds} branch builds.");
Console.WriteLine($"There were {(mainBuilds + branchBuilds)/(float)mainBuilds:F2} builds per merged branch on average.");
Console.WriteLine($"There were {manualRetries} manual retries ({manualRetries/(float)mainBuilds:F2} per PR)");
foreach (var branch in repo.Branches)
{
    buildStatus.TryGetValue(branch.Value, out var status);
    Console.WriteLine($"  Branch {branch.Key} at commit {branch.Value} is {status}");
}

public enum BuildStatus { Success, Failure, Retrying }