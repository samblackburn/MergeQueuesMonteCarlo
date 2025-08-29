namespace MergeQueuesMonteCarlo;

internal static class Extensions
{
    public static IEnumerable<(Event, DateTime)> ToAbsolute(
        this IEnumerable<(Event evt, TimeSpan duration)> delayedEvents,
        DateTime baseTime) =>
        delayedEvents.Select(eventAndDuration =>
            (e: eventAndDuration.evt, baseTime + eventAndDuration.duration));
}