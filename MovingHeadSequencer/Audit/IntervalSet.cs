namespace MovingHeadSequencer.Audit;

internal readonly record struct TimeInterval(int Start, int End)
{
    public int Duration => End - Start;
}

internal static class IntervalSet
{
    public static IReadOnlyList<TimeInterval> Merge(IEnumerable<TimeInterval> intervals)
    {
        var sorted = intervals.Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ThenBy(interval => interval.End)
            .ToArray();
        var merged = new List<TimeInterval>();
        foreach (var interval in sorted)
        {
            if (merged.Count == 0 || interval.Start > merged[^1].End)
            {
                merged.Add(interval);
            }
            else if (interval.End > merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = interval.End };
            }
        }
        return merged;
    }

    public static int Coverage(IEnumerable<TimeInterval> intervals) =>
        Merge(intervals).Sum(interval => interval.Duration);

    public static int IntersectionCoverage(
        IEnumerable<TimeInterval> left,
        IEnumerable<TimeInterval> right) => Intersect(left, right).Sum(interval => interval.Duration);

    public static IReadOnlyList<TimeInterval> Intersect(
        IEnumerable<TimeInterval> left,
        IEnumerable<TimeInterval> right)
    {
        var a = Merge(left);
        var b = Merge(right);
        var leftIndex = 0;
        var rightIndex = 0;
        var intersections = new List<TimeInterval>();
        while (leftIndex < a.Count && rightIndex < b.Count)
        {
            var start = Math.Max(a[leftIndex].Start, b[rightIndex].Start);
            var end = Math.Min(a[leftIndex].End, b[rightIndex].End);
            if (end > start)
            {
                intersections.Add(new TimeInterval(start, end));
            }
            if (a[leftIndex].End < b[rightIndex].End)
            {
                leftIndex++;
            }
            else
            {
                rightIndex++;
            }
        }
        return intersections;
    }

    public static IReadOnlyList<TimeInterval> Complement(
        IEnumerable<TimeInterval> intervals,
        int duration) => Gaps(intervals, duration, 0);

    public static IReadOnlyList<TimeInterval> Gaps(IEnumerable<TimeInterval> intervals, int duration, int minimumDuration)
    {
        var gaps = new List<TimeInterval>();
        var cursor = 0;
        foreach (var interval in Merge(intervals))
        {
            if (interval.Start - cursor >= minimumDuration)
            {
                gaps.Add(new TimeInterval(cursor, interval.Start));
            }
            cursor = Math.Max(cursor, interval.End);
        }
        if (duration - cursor >= minimumDuration)
        {
            gaps.Add(new TimeInterval(cursor, duration));
        }
        return gaps;
    }
}