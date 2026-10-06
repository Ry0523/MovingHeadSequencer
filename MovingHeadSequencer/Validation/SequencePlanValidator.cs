using System.Text.RegularExpressions;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Domain;
using MovingHeadSequencer.Sequences;

namespace MovingHeadSequencer.Validation;

internal enum ValidationSeverity
{
    Warning,
    Error,
}

internal sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    int? Start = null,
    int? End = null);

internal sealed record ValidationMetrics(
    int DurationMs,
    double PanDynamicPercent,
    double TiltDynamicPercent,
    int PanMinimum,
    int PanMaximum,
    int TiltMinimum,
    int TiltMaximum,
    double DimmerVisiblePercent,
    double ShutterOpenPercent,
    int MaximumPanDelta,
    int MaximumTiltDelta,
    int BlackoutWindowCount);

internal sealed record SequenceValidationReport(
    string Sequence,
    string Output,
    int HeadCount,
    string FixtureProfile,
    ValidationMetrics Metrics,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors(bool treatWarningsAsErrors) =>
        Issues.Any(issue => issue.Severity == ValidationSeverity.Error) ||
        (treatWarningsAsErrors && Issues.Count > 0);
}

internal static partial class SequencePlanValidator
{
    public static SequenceValidationReport Validate(
        SequencePlan plan,
        ResolvedGenerationSettings settings,
        MovingHeadLayout? layout)
    {
        var issues = new List<ValidationIssue>();
        ValidateTrack(plan.PanCues, plan.Duration, settings.HeadCount, "pan", issues);
        ValidateTrack(plan.TiltCues, plan.Duration, settings.HeadCount, "tilt", issues);
        if (!plan.ImportedSourceControls)
        {
            ValidatePositionContinuity(plan.PanCues, "pan", issues);
            ValidatePositionContinuity(plan.TiltCues, "tilt", issues);
        }
        ValidateMotion(plan.PanCues, settings.FixtureProfile.PanFor,
            plan.ImportedSourceControls ? 255 : settings.Validation.MaximumPanDelta,
            plan.ImportedSourceControls ? 0 : settings.Validation.MinimumMovementDurationMs, "pan", issues);
        ValidateMotion(plan.TiltCues, settings.FixtureProfile.TiltFor,
            plan.ImportedSourceControls ? 255 : settings.Validation.MaximumTiltDelta,
            plan.ImportedSourceControls ? 0 : settings.Validation.MinimumMovementDurationMs, "tilt", issues);
        ValidateDimmerAndShutter(plan, issues);
        ValidateLayout(layout, settings, issues);

        var panDynamic = DynamicCoverage(plan.PanCues);
        var tiltDynamic = DynamicCoverage(plan.TiltCues);
        var highestDynamicPercent = Math.Max(Percent(panDynamic, plan.Duration), Percent(tiltDynamic, plan.Duration));
        if (!plan.ImportedSourceControls &&
            highestDynamicPercent > settings.Validation.MaximumDynamicMotionPercent)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "movement-density",
                $"Dynamic movement is {highestDynamicPercent:0.0}% of the sequence; configured maximum is " +
                $"{settings.Validation.MaximumDynamicMotionPercent:0.0}%."));
        }

        var visibleDimmer = Merge(plan.DimmerCues.Where(HasVisibleIntensity).Select(ToInterval));
        var shutter = Merge(plan.ShutterEvents.Select(effect => new TimeRange(effect.Start, effect.End)));
        var metrics = new ValidationMetrics(
            plan.Duration,
            Percent(panDynamic, plan.Duration),
            Percent(tiltDynamic, plan.Duration),
            settings.FixtureProfile.Pan.Minimum,
            settings.FixtureProfile.Pan.Maximum,
            settings.FixtureProfile.Tilt.Minimum,
            settings.FixtureProfile.Tilt.Maximum,
            Percent(Coverage(visibleDimmer), plan.Duration),
            Percent(Coverage(shutter), plan.Duration),
            MaximumDelta(plan.PanCues),
            MaximumDelta(plan.TiltCues),
            Gaps(visibleDimmer, plan.Duration).Count);

        return new SequenceValidationReport(
            plan.SourceFileName,
            plan.OutputFileName,
            settings.HeadCount,
            settings.FixtureProfile.Name,
            metrics,
            issues);
    }

    private static void ValidateTrack(
        IReadOnlyList<Cue> cues,
        int duration,
        int headCount,
        string axis,
        ICollection<ValidationIssue> issues)
    {
        var previousEnd = 0;
        foreach (var cue in cues)
        {
            if (cue.Start != previousEnd)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{axis}-continuity",
                    $"{axis} cue starts at {cue.Start} ms; previous cue ends at {previousEnd} ms.",
                    previousEnd,
                    cue.Start));
            }
            if (cue.Keys.Count != headCount)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{axis}-head-count",
                    $"{axis} cue has {cue.Keys.Count} values; expected {headCount}.",
                    cue.Start,
                    cue.End));
            }
            previousEnd = cue.End;
        }
        if (previousEnd != duration)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                $"{axis}-duration",
                $"{axis} track ends at {previousEnd} ms; sequence ends at {duration} ms."));
        }
    }

    private static void ValidateMotion(
        IEnumerable<Cue> cues,
        Func<int, AxisProfileSettings> axisFor,
        int maximumDelta,
        int minimumDuration,
        string axisName,
        ICollection<ValidationIssue> issues)
    {
        foreach (var cue in cues)
        {
            for (var index = 0; index < cue.Keys.Count; index++)
            {
                var key = cue.Keys[index];
                var axis = axisFor(index);
                var values = ParseMotionValues(key).ToArray();
                foreach (var value in values.Where(value => value < axis.Minimum || value > axis.Maximum))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"unsafe-{axisName}-range",
                        $"{axisName} value {value} is outside fixture range {axis.Minimum}-{axis.Maximum}.",
                        cue.Start,
                        cue.End));
                }
                if (values.Length > 1)
                {
                    var delta = AdjacentDeltas(values).DefaultIfEmpty().Max();
                    if (delta > maximumDelta)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            $"unsafe-{axisName}-travel",
                            $"{axisName} travel {delta} exceeds configured maximum {maximumDelta}.",
                            cue.Start,
                            cue.End));
                    }
                    if (minimumDuration > 0 && cue.End - cue.Start < minimumDuration)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            $"unsafe-{axisName}-duration",
                            $"{axisName} movement lasts {cue.End - cue.Start} ms; minimum is {minimumDuration} ms.",
                            cue.Start,
                            cue.End));
                    }
                }
            }
        }
    }

    private static void ValidatePositionContinuity(
        IReadOnlyList<Cue> cues,
        string axis,
        ICollection<ValidationIssue> issues)
    {
        for (var cueIndex = 1; cueIndex < cues.Count; cueIndex++)
        {
            var previous = cues[cueIndex - 1];
            var current = cues[cueIndex];
            var jumpingHeads = new List<int>();
            var maximumJump = 0;
            for (var headIndex = 0; headIndex < Math.Min(previous.Keys.Count, current.Keys.Count); headIndex++)
            {
                var previousValues = ParseMotionValues(previous.Keys[headIndex]).ToArray();
                var currentValues = ParseMotionValues(current.Keys[headIndex]).ToArray();
                if (previousValues.Length == 0 || currentValues.Length == 0)
                {
                    continue;
                }
                var jump = Math.Abs(previousValues[^1] - currentValues[0]);
                if (jump <= 1)
                {
                    continue;
                }
                jumpingHeads.Add(headIndex + 1);
                maximumJump = Math.Max(maximumJump, jump);
            }
            if (jumpingHeads.Count > 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"{axis}-position-jump",
                    $"{axis} jumps by up to {maximumJump} at the cue boundary on heads " +
                    $"{string.Join(", ", jumpingHeads)}.",
                    current.Start,
                    current.Start));
            }
        }
    }

    private static void ValidateDimmerAndShutter(SequencePlan plan, ICollection<ValidationIssue> issues)
    {
        ValidateNoOverlaps(plan.DimmerCues.Select(ToInterval), "dimmer-overlap", issues);
        ValidateNoOverlaps(
            plan.ShutterEvents.Select(effect => new TimeRange(effect.Start, effect.End)),
            "shutter-overlap",
            issues);
        if (plan.DimmerCues.Count == 0 || plan.ShutterEvents.Count == 0)
        {
            return;
        }

        var shutter = Merge(plan.ShutterEvents.Select(effect => new TimeRange(effect.Start, effect.End)));
        foreach (var cue in plan.DimmerCues.Where(HasVisibleIntensity))
        {
            if (!shutter.Any(window => window.Start <= cue.Start && window.End >= cue.End))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "dimmer-shutter-mismatch",
                    "Visible dimmer cue is not fully covered by an open shutter window.",
                    cue.Start,
                    cue.End));
            }
        }
    }

    private static void ValidateLayout(
        MovingHeadLayout? layout,
        ResolvedGenerationSettings settings,
        ICollection<ValidationIssue> issues)
    {
        if (layout is null)
        {
            return;
        }
        if (layout.Fixtures.Count != settings.HeadCount)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "layout-head-count",
                $"Layout has {layout.Fixtures.Count} selected fixtures; generation expects {settings.HeadCount}."));
        }
        foreach (var role in Enum.GetValues<ControlRole>().Where(role => !layout.Controls.Any(control => control.Role == role)))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "layout-control-missing",
                $"Layout has no discovered {role} control model."));
        }
        foreach (var warning in layout.Warnings)
        {
            issues.Add(new ValidationIssue(
                settings.AllowLayoutWarnings ? ValidationSeverity.Warning : ValidationSeverity.Error,
                "layout-warning",
                warning));
        }
    }

    private static void ValidateNoOverlaps(
        IEnumerable<TimeRange> intervals,
        string code,
        ICollection<ValidationIssue> issues)
    {
        var previousEnd = 0;
        foreach (var interval in intervals.OrderBy(interval => interval.Start))
        {
            if (interval.Start < previousEnd)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    code,
                    $"Intervals overlap at {interval.Start} ms.",
                    interval.Start,
                    Math.Min(previousEnd, interval.End)));
            }
            previousEnd = Math.Max(previousEnd, interval.End);
        }
    }

    private static int DynamicCoverage(IEnumerable<Cue> cues) =>
        Coverage(Merge(cues.Where(IsDynamic).Select(ToInterval)));

    private static bool IsDynamic(Cue cue) => cue.Keys.Any(key => ParseMotionValues(key).Skip(1).Any());

    private static int MaximumDelta(IEnumerable<Cue> cues) =>
        cues.SelectMany(cue => cue.Keys)
            .Select(key => AdjacentDeltas(ParseMotionValues(key)).DefaultIfEmpty().Max())
            .DefaultIfEmpty()
            .Max();

    private static IEnumerable<int> ParseMotionValues(string key) =>
        MotionValueRegex().Matches(key).Select(match => int.Parse(match.Value));

    private static IEnumerable<int> AdjacentDeltas(IEnumerable<int> values)
    {
        int? previous = null;
        foreach (var value in values)
        {
            if (previous is not null)
            {
                yield return Math.Abs(value - previous.Value);
            }
            previous = value;
        }
    }

    private static bool HasVisibleIntensity(Cue cue) =>
        cue.Keys.Any(key => !string.IsNullOrEmpty(key) && key != "D0");

    private static TimeRange ToInterval(Cue cue) => new(cue.Start, cue.End);

    private static IReadOnlyList<TimeRange> Merge(IEnumerable<TimeRange> intervals)
    {
        var merged = new List<TimeRange>();
        foreach (var interval in intervals.Where(interval => interval.End > interval.Start).OrderBy(interval => interval.Start))
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

    private static IReadOnlyList<TimeRange> Gaps(IReadOnlyList<TimeRange> intervals, int duration)
    {
        var gaps = new List<TimeRange>();
        var cursor = 0;
        foreach (var interval in intervals)
        {
            if (interval.Start > cursor)
            {
                gaps.Add(new TimeRange(cursor, interval.Start));
            }
            cursor = Math.Max(cursor, interval.End);
        }
        if (cursor < duration)
        {
            gaps.Add(new TimeRange(cursor, duration));
        }
        return gaps;
    }

    private static int Coverage(IEnumerable<TimeRange> intervals) => intervals.Sum(interval => interval.End - interval.Start);

    private static double Percent(int value, int duration) =>
        duration == 0 ? 0 : Math.Round(100d * value / duration, 1);

    private sealed record TimeRange(int Start, int End);

    [GeneratedRegex("\\d+", RegexOptions.CultureInvariant)]
    private static partial Regex MotionValueRegex();
}