using System.Globalization;
using System.Text.RegularExpressions;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Sequences;

namespace MovingHeadSequencer.Preview;

internal enum PreviewCurveKind
{
    Hold,
    Linear,
    Bounce,
    Custom,
}

internal sealed record PreviewCurvePoint(double Time, double Value);

internal sealed record PreviewCurveSegment(
    int StartMs,
    int EndMs,
    PreviewCurveKind Kind,
    double StartValue,
    double EndValue,
    double? MiddleValue = null,
    IReadOnlyList<PreviewCurvePoint>? Points = null);

internal sealed record PreviewAxisRange(int Minimum, int Maximum, int Park);

internal sealed record PreviewFixtureTrack(
    int Number,
    string Name,
    PreviewAxisRange PanRange,
    PreviewAxisRange TiltRange,
    IReadOnlyList<PreviewCurveSegment> Pan,
    IReadOnlyList<PreviewCurveSegment> Tilt,
    IReadOnlyList<PreviewCurveSegment> Dimmer);

internal sealed record CompiledPreview(
    int DurationMs,
    IReadOnlyList<PreviewFixtureTrack> Fixtures,
    IReadOnlyList<RootEffectEvent> ShutterWindows);

internal static partial class PreviewCompiler
{
    public static CompiledPreview Compile(
        SequencePlan plan,
        int headCount,
        FixtureProfileSettings profile,
        IReadOnlyList<string>? fixtureNames = null)
    {
        fixtureNames ??= Enumerable.Range(1, headCount).Select(index => $"MH {index}").ToArray();
        if (fixtureNames.Count != headCount)
        {
            throw new InvalidDataException(
                $"Preview received {fixtureNames.Count} fixture names; expected {headCount}.");
        }

        var fixtures = Enumerable.Range(0, headCount)
            .Select(index =>
            {
                var pan = profile.PanFor(index);
                var tilt = profile.TiltFor(index);
                return new PreviewFixtureTrack(
                    index + 1,
                    fixtureNames[index],
                    new PreviewAxisRange(pan.Minimum, pan.Maximum, Transform(pan.Park, pan)),
                    new PreviewAxisRange(tilt.Minimum, tilt.Maximum, Transform(tilt.Park, tilt)),
                    CompileTrack(plan.PanCues, index, defaultValue: Transform(pan.Park, pan)),
                    CompileTrack(plan.TiltCues, index, defaultValue: Transform(tilt.Park, tilt)),
                        CompileTrack(plan.DimmerCues, index, defaultValue: 0));
                    })
            .ToArray();
        return new CompiledPreview(plan.Duration, fixtures, plan.ShutterEvents);
    }

    public static double Evaluate(PreviewCurveSegment segment, int timeMs)
    {
        if (segment.EndMs <= segment.StartMs || segment.Kind == PreviewCurveKind.Hold)
        {
            return segment.StartValue;
        }
        var progress = Math.Clamp(
            (double)(timeMs - segment.StartMs) / (segment.EndMs - segment.StartMs),
            0,
            1);
        return segment.Kind switch
        {
            PreviewCurveKind.Linear => Lerp(segment.StartValue, segment.EndValue, progress),
            PreviewCurveKind.Bounce when progress <= 0.5 =>
                Lerp(segment.StartValue, segment.MiddleValue ?? segment.EndValue, progress * 2),
            PreviewCurveKind.Bounce =>
                Lerp(segment.MiddleValue ?? segment.StartValue, segment.EndValue, (progress - 0.5) * 2),
            PreviewCurveKind.Custom => EvaluateCustom(segment.Points ?? [], progress, segment.StartValue),
            _ => segment.StartValue,
        };
    }

    private static PreviewCurveSegment[] CompileTrack(
        IEnumerable<Cue> cues,
        int headIndex,
        double defaultValue) =>
        cues.Select(cue => Parse(cue.Start, cue.End, cue.Keys[headIndex], defaultValue)).ToArray();

    private static PreviewCurveSegment Parse(int start, int end, string key, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new PreviewCurveSegment(start, end, PreviewCurveKind.Hold, defaultValue, defaultValue);
        }
        if (MotionCurveKey.TryParse(key, out var custom))
        {
            var points = custom.Points
                .Select(point => new PreviewCurvePoint(point.Time, point.Value))
                .ToArray();
            return new PreviewCurveSegment(
                start,
                end,
                PreviewCurveKind.Custom,
                custom.Start,
                custom.End,
                custom.Middle,
                points);
        }
        var values = NumberRegex().Matches(key)
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        return values.Length switch
        {
            1 => new PreviewCurveSegment(start, end, PreviewCurveKind.Hold, values[0], values[0]),
            2 => new PreviewCurveSegment(start, end, PreviewCurveKind.Linear, values[0], values[1]),
            3 => new PreviewCurveSegment(start, end, PreviewCurveKind.Bounce, values[0], values[2], values[1]),
            _ => throw new InvalidDataException($"Unsupported preview movement key '{key}'."),
        };
    }

    private static double Lerp(double start, double end, double progress) =>
        start + ((end - start) * progress);

    private static double EvaluateCustom(
        IReadOnlyList<PreviewCurvePoint> points,
        double progress,
        double fallback)
    {
        if (points.Count == 0)
        {
            return fallback;
        }
        for (var index = 1; index < points.Count; index++)
        {
            if (progress > points[index].Time)
            {
                continue;
            }
            var left = points[index - 1];
            var right = points[index];
            var span = right.Time - left.Time;
            return span <= 0
                ? right.Value
                : Lerp(left.Value, right.Value, (progress - left.Time) / span);
        }
        return points[^1].Value;
    }

    private static int Transform(int value, AxisProfileSettings axis) =>
        axis.Inverted ? axis.Minimum + axis.Maximum - value : value;

    [GeneratedRegex("\\d+(?:\\.\\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}