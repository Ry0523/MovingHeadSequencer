using System.Globalization;
using System.Text.RegularExpressions;

namespace MovingHeadSequencer.Choreography;

internal static partial class CueCurveSlicer
{
    public static bool IsDynamicKey(string key) => NumberRegex().Matches(key).Count > 1;

    public static string EndKey(string key)
    {
        var values = NumberRegex().Matches(key);
        return values.Count == 0 ? key : $"{key[0]}{values[^1].Value}";
    }

    public static string[] SliceTrack(
        IReadOnlyList<Cue> cues,
        int startMs,
        int endMs,
        IReadOnlyList<string> fallbackKeys)
    {
        var cue = cues.FirstOrDefault(candidate => candidate.Start <= startMs && candidate.End >= endMs);
        if (cue is null)
        {
            return [.. fallbackKeys];
        }
        return cue.Keys
            .Select((key, index) => SliceKey(
                string.IsNullOrWhiteSpace(key) ? fallbackKeys[index] : key,
                cue.Start,
                cue.End,
                startMs,
                endMs))
            .ToArray();
    }

    public static string SliceKey(
        string key,
        int sourceStartMs,
        int sourceEndMs,
        int sliceStartMs,
        int sliceEndMs)
    {
        if (sliceStartMs < sourceStartMs || sliceEndMs > sourceEndMs || sliceEndMs <= sliceStartMs)
        {
            throw new InvalidDataException(
                $"Invalid curve slice {sliceStartMs}-{sliceEndMs} for {sourceStartMs}-{sourceEndMs}.");
        }
        if (sliceStartMs == sourceStartMs && sliceEndMs == sourceEndMs)
        {
            return key;
        }

        var values = NumberRegex().Matches(key)
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length == 0)
        {
            return key;
        }
        var prefix = key[0];
        if (values.Length == 1)
        {
            return $"{prefix}{Round(values[0])}";
        }

        var sliceStart = Evaluate(values, sourceStartMs, sourceEndMs, sliceStartMs);
        var sliceEnd = Evaluate(values, sourceStartMs, sourceEndMs, sliceEndMs);
        if (values.Length == 3)
        {
            var midpoint = sourceStartMs + ((sourceEndMs - sourceStartMs) / 2d);
            if (sliceStartMs < midpoint && sliceEndMs > midpoint)
            {
                return Build(prefix, sliceStart, values[1], sliceEnd, bounce: true);
            }
        }
        return Build(prefix, sliceStart, sliceEnd, null, bounce: false);
    }

    private static double Evaluate(
        IReadOnlyList<double> values,
        int startMs,
        int endMs,
        int timeMs)
    {
        var progress = endMs <= startMs
            ? 0
            : Math.Clamp((double)(timeMs - startMs) / (endMs - startMs), 0, 1);
        if (values.Count == 2)
        {
            return Lerp(values[0], values[1], progress);
        }
        if (values.Count == 3)
        {
            return progress <= 0.5
                ? Lerp(values[0], values[1], progress * 2)
                : Lerp(values[1], values[2], (progress - 0.5) * 2);
        }
        return values[0];
    }

    private static string Build(
        char prefix,
        double first,
        double second,
        double? third,
        bool bounce)
    {
        var a = Round(first);
        var b = Round(second);
        if (!bounce || third is null)
        {
            return a == b ? $"{prefix}{a}" : $"{prefix}{a}_{b}";
        }
        var c = Round(third.Value);
        return $"{prefix}B{a}_{b}_{c}";
    }

    private static int Round(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static double Lerp(double start, double end, double progress) =>
        start + ((end - start) * progress);

    [GeneratedRegex("\\d+(?:\\.\\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}