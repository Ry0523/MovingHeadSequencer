using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Choreography;

internal enum PanPattern
{
    Park,
    Fan,
    OppositeFan,
    OpenFan,
    CloseFan,
    CloseOppositeFan,
    Cross,
    Uncross,
    Bounce,
}

internal enum TiltPattern
{
    Park,
    High,
    Rise,
    Fall,
    Bounce,
    InverseBounce,
}

internal enum DimmerPattern
{
    All100,
    All75,
    All70,
    All50,
    All45,
    All0,
    OuterHigh,
    CenterHigh,
    OuterOnly,
    CenterOnly,
    Fade75,
    Fade30,
}

internal enum RhythmPattern
{
    Off,
    TiltLiftEveryBeat,
    TiltPulseEveryHalfBeat,
    TiltBounceEveryBeat,
    TiltBounceEveryTwoBeats,
    TiltSyncopatedPulse,
    TiltOddEvenEveryBeat,
    TiltCenterOuterEveryBeat,
    TiltHeadChaseEveryBeat,
    TiltPairChaseEveryBeat,
    PanBounceEveryTwoBeats,
    PanTiltBounceEveryFourBeats,
}

internal enum MotionEnergy
{
    Subtle,
    Medium,
    Full,
    Extended,
}

internal enum MotionShape
{
    Smooth,
    Punch,
    Double,
}

internal enum MotionPhase
{
    Together,
    LeftToRight,
    CenterOut,
    AlternatingPairs,
}

internal enum IntensityEnvelope
{
    Steady,
    FadeIn,
    FadeOut,
    Pulse,
    Turnaround,
    Flash,
    RiseAndHold,
    TexturedPulse,
}

internal static class PatternFactory
{
    public static int[] EvenPanValues(int headCount, FixtureProfileSettings? profile = null)
    {
        ValidateHeadCount(headCount);
        profile ??= new FixtureProfileSettings();
        var ratios = headCount == 4
            ? new[] { 0d, 0.25d, 0.75d, 1d }
            : Enumerable.Range(0, headCount).Select(index => index / (headCount - 1d));
        return ratios.Select((ratio, index) =>
            {
                var axis = profile.PanFor(index);
                var value = (int)Math.Round(axis.Outer + ((axis.Center - axis.Outer) * ratio));
                return Transform(value, axis);
            })
            .ToArray();
    }

    public static int[] SymmetricValues(int headCount, int outerValue, int centerValue)
    {
        ValidateHeadCount(headCount);
        return Enumerable.Range(0, headCount)
            .Select(index =>
            {
                var ratio = SymmetricRatio(headCount, index);
                return (int)Math.Round(
                    outerValue + ((centerValue - outerValue) * ratio),
                    MidpointRounding.AwayFromZero);
            })
            .ToArray();
    }

    public static string[] Pan(
        PanPattern pattern,
        int headCount,
        FixtureProfileSettings? profile = null,
        MotionEnergy energy = MotionEnergy.Full)
    {
        profile ??= new FixtureProfileSettings();
        var fanValues = EvenPanValues(headCount, profile);
        var ratios = headCount == 4
            ? new[] { 0d, 0.25d, 0.75d, 1d }
            : Enumerable.Range(0, headCount).Select(index => index / (headCount - 1d)).ToArray();
        return Enumerable.Range(0, headCount)
            .Select(index =>
            {
                var axis = profile.PanFor(index);
                var park = Transform(axis.Park, axis);
                var fanValue = fanValues[index];
                var opposite = (int)Math.Round(axis.Outer + ((axis.Center - axis.Outer) * (1d - ratios[index])));
                var oppositeValue = Transform(opposite, axis);
                var bounceValue = ScaleExcursion(fanValue, oppositeValue, axis, energy);
                return pattern switch
                {
                    PanPattern.Park => $"P{park}",
                    PanPattern.Fan => $"P{fanValue}",
                    PanPattern.OppositeFan => $"P{oppositeValue}",
                    PanPattern.OpenFan => $"P{park}_{fanValue}",
                    PanPattern.CloseFan => $"P{fanValue}_{park}",
                    PanPattern.CloseOppositeFan => $"P{oppositeValue}_{park}",
                    PanPattern.Cross => $"P{fanValue}_{oppositeValue}",
                    PanPattern.Uncross => $"P{oppositeValue}_{fanValue}",
                    PanPattern.Bounce => $"PB{fanValue}_{bounceValue}_{fanValue}",
                    _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
                };
            })
            .ToArray();
    }

    public static string[] PanRhythmBounce(
        PanPattern anchor,
        int headCount,
        FixtureProfileSettings? profile = null,
        MotionEnergy energy = MotionEnergy.Full)
    {
        if (anchor is not PanPattern.Fan and not PanPattern.OppositeFan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(anchor),
                "Rhythmic pan bounce must anchor at Fan or OppositeFan.");
        }
        profile ??= new FixtureProfileSettings();
        var fan = Pan(PanPattern.Fan, headCount, profile);
        var opposite = Pan(PanPattern.OppositeFan, headCount, profile);
        return Enumerable.Range(0, headCount)
            .Select(index =>
            {
                var axis = profile.PanFor(index);
                var start = anchor == PanPattern.Fan
                    ? ReadValue(fan[index])
                    : ReadValue(opposite[index]);
                var target = anchor == PanPattern.Fan
                    ? ReadValue(opposite[index])
                    : ReadValue(fan[index]);
                var middle = ScaleExcursion(start, target, axis, energy);
                return $"PB{start}_{middle}_{start}";
            })
            .ToArray();
    }

    public static string[] Tilt(
        TiltPattern pattern,
        int headCount,
        FixtureProfileSettings? profile = null,
        MotionEnergy energy = MotionEnergy.Full)
    {
        profile ??= new FixtureProfileSettings();
        return Enumerable.Range(0, headCount).Select(index =>
        {
            var axis = profile.TiltFor(index);
            var ratio = SymmetricRatio(headCount, index);
            var high = (int)Math.Round(
                axis.Outer + ((axis.Center - axis.Outer) * ratio),
                MidpointRounding.AwayFromZero);
            var park = Transform(axis.Park, axis);
            var highValue = Transform(high, axis);
            var raisedValue = ScaleExcursion(park, highValue, axis, energy);
            var dippedValue = ScaleExcursion(highValue, park, axis, energy);
            return pattern switch
            {
                TiltPattern.Park => $"T{park}",
                TiltPattern.High => $"T{highValue}",
                TiltPattern.Rise => $"T{park}_{highValue}",
                TiltPattern.Fall => $"T{highValue}_{park}",
                TiltPattern.Bounce => $"TB{park}_{raisedValue}_{park}",
                TiltPattern.InverseBounce => $"TB{highValue}_{dippedValue}_{highValue}",
                _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
            };
        }).ToArray();
    }

    public static string[] TiltOddEvenBounce(
        int headCount,
        bool oddActive,
        FixtureProfileSettings? profile = null,
        MotionEnergy energy = MotionEnergy.Full)
    {
        var high = Tilt(TiltPattern.High, headCount, profile);
        var bounce = Tilt(TiltPattern.InverseBounce, headCount, profile, energy);
        return Enumerable.Range(0, headCount)
            .Select(index => ((index % 2 == 0) == oddActive) ? bounce[index] : high[index])
            .ToArray();
    }

    public static string[] TiltCenterOuterBounce(
        int headCount,
        bool outerActive,
        FixtureProfileSettings? profile = null,
        MotionEnergy energy = MotionEnergy.Full)
    {
        var high = Tilt(TiltPattern.High, headCount, profile);
        var bounce = Tilt(TiltPattern.InverseBounce, headCount, profile, energy);
        return Enumerable.Range(0, headCount)
            .Select(index =>
            {
                var isOuter = headCount == 2
                    ? index == 0
                    : SymmetricRatio(headCount, index) < 0.5d;
                return isOuter == outerActive ? bounce[index] : high[index];
            })
            .ToArray();
    }

    public static string[] TiltHeadChase(
        int headCount,
        int activeHeadIndex,
        FixtureProfileSettings? profile = null,
        MotionEnergy energy = MotionEnergy.Full)
    {
        ValidateHeadCount(headCount);
        var active = Math.Abs(activeHeadIndex % headCount);
        var high = Tilt(TiltPattern.High, headCount, profile);
        var bounce = Tilt(TiltPattern.InverseBounce, headCount, profile, energy);
        return Enumerable.Range(0, headCount)
            .Select(index => index == active ? bounce[index] : high[index])
            .ToArray();
    }

    public static string[] TiltPairChase(
        int headCount,
        int activePairIndex,
        FixtureProfileSettings? profile = null,
        MotionEnergy energy = MotionEnergy.Full)
    {
        ValidateHeadCount(headCount);
        var pairCount = (headCount + 1) / 2;
        var activePair = Math.Abs(activePairIndex % pairCount);
        var left = activePair;
        var right = headCount - 1 - activePair;
        var high = Tilt(TiltPattern.High, headCount, profile);
        var bounce = Tilt(TiltPattern.InverseBounce, headCount, profile, energy);
        return Enumerable.Range(0, headCount)
            .Select(index => index == left || index == right ? bounce[index] : high[index])
            .ToArray();
    }

    public static string[] Dimmer(DimmerPattern pattern, int headCount)
    {
        ValidateHeadCount(headCount);
        if (headCount == 4 && pattern == DimmerPattern.OuterOnly)
        {
            return ["D100", "", "", "D100"];
        }
        if (headCount == 4 && pattern == DimmerPattern.CenterOnly)
        {
            return ["", "D100", "D100", ""];
        }

        if (pattern == DimmerPattern.Fade75)
        {
            return Enumerable.Repeat("D75_30", headCount).ToArray();
        }
        if (pattern == DimmerPattern.Fade30)
        {
            return Enumerable.Repeat("D30_0", headCount).ToArray();
        }

        var values = pattern switch
        {
            DimmerPattern.All100 => Enumerable.Repeat(100, headCount).ToArray(),
            DimmerPattern.All75 => Enumerable.Repeat(75, headCount).ToArray(),
            DimmerPattern.All70 => Enumerable.Repeat(70, headCount).ToArray(),
            DimmerPattern.All50 => Enumerable.Repeat(50, headCount).ToArray(),
            DimmerPattern.All45 => Enumerable.Repeat(45, headCount).ToArray(),
            DimmerPattern.All0 => Enumerable.Repeat(0, headCount).ToArray(),
            DimmerPattern.OuterHigh => SymmetricValues(headCount, 100, 70),
            DimmerPattern.CenterHigh => SymmetricValues(headCount, 70, 100),
            DimmerPattern.OuterOnly => SymmetricValues(headCount, 100, 0),
            DimmerPattern.CenterOnly => SymmetricValues(headCount, 0, 100),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
        return values.Select(value => $"D{value}").ToArray();
    }

    private static void ValidateHeadCount(int headCount)
    {
        if (!HeadCountPolicy.IsSupported(headCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(headCount),
                $"Head count must be {HeadCountPolicy.RangeText}.");
        }
    }

    private static int ReadValue(string key) => int.Parse(key[1..]);

    private static int ScaleExcursion(
        int anchor,
        int target,
        AxisProfileSettings axis,
        MotionEnergy energy)
    {
        if (anchor == target)
        {
            return anchor;
        }
        if (energy == MotionEnergy.Extended)
        {
            return target > anchor ? axis.Maximum : axis.Minimum;
        }
        var scale = energy switch
        {
            MotionEnergy.Subtle => 0.4d,
            MotionEnergy.Medium => 0.7d,
            MotionEnergy.Full => 1d,
            _ => throw new ArgumentOutOfRangeException(nameof(energy)),
        };
        return (int)Math.Round(
            anchor + ((target - anchor) * scale),
            MidpointRounding.AwayFromZero);
    }

    private static double SymmetricRatio(int headCount, int index)
    {
        var distanceFromOuter = Math.Min(index, headCount - 1 - index);
        var maximumDistance = Math.Max(1, (headCount - 1) / 2);
        return (double)distanceFromOuter / maximumDistance;
    }

    private static int Transform(int value, AxisProfileSettings axis) =>
        axis.Inverted ? axis.Minimum + axis.Maximum - value : value;
}