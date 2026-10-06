using System.Globalization;
using System.Text.RegularExpressions;

namespace MovingHeadSequencer.Choreography;

internal sealed record MotionCurvePoint(double Time, double Value);

internal sealed record MotionCurveDefinition(
    char Axis,
    MotionShape Shape,
    char PhaseCode,
    int Start,
    int Middle,
    int End)
{
    public IReadOnlyList<MotionCurvePoint> Points
    {
        get
        {
            var local = Shape switch
            {
                MotionShape.Smooth => new[]
                {
                    new MotionCurvePoint(0, Start),
                    new MotionCurvePoint(0.5, Middle),
                    new MotionCurvePoint(1, End),
                },
                MotionShape.Punch =>
                [
                    new MotionCurvePoint(0, Start),
                    new MotionCurvePoint(0.32, Middle),
                    new MotionCurvePoint(0.48, Middle),
                    new MotionCurvePoint(1, End),
                ],
                MotionShape.Double =>
                [
                    new MotionCurvePoint(0, Start),
                    new MotionCurvePoint(0.25, Middle),
                    new MotionCurvePoint(0.5, Start),
                    new MotionCurvePoint(0.75, Middle),
                    new MotionCurvePoint(1, End),
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(Shape)),
            };
            var (offset, span) = PhaseCode == 'Z'
                ? (0d, 1d)
                : ((PhaseCode - 'A') / 11d * 0.35d, 0.65d);
            var points = new List<MotionCurvePoint>();
            if (offset > 0)
            {
                points.Add(new MotionCurvePoint(0, Start));
            }
            points.AddRange(local.Select(point =>
                new MotionCurvePoint(offset + (point.Time * span), point.Value)));
            if (offset + span < 1)
            {
                points.Add(new MotionCurvePoint(1, End));
            }
            return points;
        }
    }
}

internal static partial class MotionCurveKey
{
    public static string ApplyStyle(
        string key,
        MotionShape shape,
        MotionPhase phase,
        int fixtureIndex,
        int fixtureCount)
    {
        var match = BounceKeyRegex().Match(key);
        if (!match.Success)
        {
            return key;
        }
        var phaseCode = ResolvePhaseCode(phase, fixtureIndex, fixtureCount);
        if (shape == MotionShape.Smooth && phaseCode == 'Z')
        {
            return key;
        }
        var shapeCode = shape switch
        {
            MotionShape.Smooth => 'S',
            MotionShape.Punch => 'P',
            MotionShape.Double => 'D',
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{match.Groups[1].Value}C{shapeCode}{phaseCode}" +
            $"{match.Groups[2].Value}_{match.Groups[3].Value}_{match.Groups[4].Value}");
    }

    public static bool TryParse(string key, out MotionCurveDefinition definition)
    {
        var match = CustomKeyRegex().Match(key);
        if (!match.Success)
        {
            definition = null!;
            return false;
        }
        definition = new MotionCurveDefinition(
            match.Groups[1].Value[0],
            match.Groups[2].Value[0] switch
            {
                'S' => MotionShape.Smooth,
                'P' => MotionShape.Punch,
                'D' => MotionShape.Double,
                _ => throw new InvalidDataException($"Unknown motion shape in '{key}'."),
            },
            match.Groups[3].Value[0],
            Parse(match, 4),
            Parse(match, 5),
            Parse(match, 6));
        return true;
    }

    private static char ResolvePhaseCode(
        MotionPhase phase,
        int fixtureIndex,
        int fixtureCount)
    {
        if (phase == MotionPhase.Together)
        {
            return 'Z';
        }
        var ratio = phase switch
        {
            MotionPhase.LeftToRight => fixtureCount <= 1
                ? 0
                : fixtureIndex / (fixtureCount - 1d),
            MotionPhase.CenterOut => CenterOutRatio(fixtureIndex, fixtureCount),
            MotionPhase.AlternatingPairs => (fixtureIndex / 2) % 2 == 0 ? 0 : 0.64d,
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
        return (char)('A' + (int)Math.Round(
            Math.Clamp(ratio, 0, 1) * 11,
            MidpointRounding.AwayFromZero));
    }

    private static double CenterOutRatio(int fixtureIndex, int fixtureCount)
    {
        var center = (fixtureCount - 1) / 2d;
        var distance = Math.Abs(fixtureIndex - center);
        var minimum = fixtureCount % 2 == 0 ? 0.5d : 0d;
        return center <= minimum ? 0 : (distance - minimum) / (center - minimum);
    }

    private static int Parse(Match match, int group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex("^([PT])B(\\d+)_(\\d+)_(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BounceKeyRegex();

    [GeneratedRegex("^([PT])C([SPD])([A-LZ])(\\d+)_(\\d+)_(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomKeyRegex();
}