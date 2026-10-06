using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Sequences;

internal static class LilJonSequenceDefinition
{
    public static SequencePlan Create(int headCount)
        => Create(headCount, new FixtureProfileSettings());

    public static SequencePlan Create(int headCount, FixtureProfileSettings fixtureProfile)
    {
        Cue C(int start, int end, IReadOnlyList<string> keys) => Cue.Create(start, end, keys, headCount);

        var park = PatternFactory.Pan(PanPattern.Park, headCount, fixtureProfile);
        var fan = PatternFactory.Pan(PanPattern.Fan, headCount, fixtureProfile);
        var oppositeFan = PatternFactory.Pan(PanPattern.OppositeFan, headCount, fixtureProfile);
        var openFan = PatternFactory.Pan(PanPattern.OpenFan, headCount, fixtureProfile);
        var closeFan = PatternFactory.Pan(PanPattern.CloseFan, headCount, fixtureProfile);
        var closeOppositeFan = PatternFactory.Pan(PanPattern.CloseOppositeFan, headCount, fixtureProfile);
        var cross = PatternFactory.Pan(PanPattern.Cross, headCount, fixtureProfile);
        var uncross = PatternFactory.Pan(PanPattern.Uncross, headCount, fixtureProfile);

        Cue[] panCues =
        [
            C(0, 9050, park),
            C(9050, 10000, openFan),
            C(10000, 14775, fan),
            C(14775, 18850, cross),
            C(18850, 28875, oppositeFan),
            C(28875, 33675, uncross),
            C(33675, 47950, fan),
            C(47950, 52100, closeFan),
            C(52100, 52850, park),
            C(52850, 55425, openFan),
            C(55425, 62475, fan),
            C(62475, 67600, cross),
            C(67600, 71625, oppositeFan),
            C(71625, 72025, closeOppositeFan),
            C(72025, 79225, openFan),
            C(79225, 88575, fan),
            C(88575, 91125, cross),
            C(91125, 95975, oppositeFan),
            C(95975, 100775, uncross),
            C(100775, 115300, fan),
            C(115300, 120100, cross),
            C(120100, 133950, oppositeFan),
            C(133950, 138750, uncross),
            C(138750, 153650, fan),
            C(153650, 158550, closeFan),
            C(158550, 166000, park),
        ];

        var tiltPark = PatternFactory.Tilt(TiltPattern.Park, headCount, fixtureProfile);
        var tiltHigh = PatternFactory.Tilt(TiltPattern.High, headCount, fixtureProfile);
        var tiltRise = PatternFactory.Tilt(TiltPattern.Rise, headCount, fixtureProfile);
        var tiltFall = PatternFactory.Tilt(TiltPattern.Fall, headCount, fixtureProfile);
        var tiltInverseBounce = PatternFactory.Tilt(TiltPattern.InverseBounce, headCount, fixtureProfile);
        Cue[] tiltCues =
        [
            C(0, 9050, tiltPark),
            C(9050, 10000, tiltRise),
            C(10000, 14775, tiltHigh),
            C(14775, 18850, tiltInverseBounce),
            C(18850, 28875, tiltHigh),
            C(28875, 33675, tiltInverseBounce),
            C(33675, 47950, tiltHigh),
            C(47950, 52100, tiltFall),
            C(52100, 52850, tiltPark),
            C(52850, 55425, tiltRise),
            C(55425, 62475, tiltHigh),
            C(62475, 67600, tiltInverseBounce),
            C(67600, 71625, tiltHigh),
            C(71625, 72025, tiltFall),
            C(72025, 79225, tiltRise),
            C(79225, 88575, tiltHigh),
            C(88575, 91125, tiltInverseBounce),
            C(91125, 95975, tiltHigh),
            C(95975, 100775, tiltInverseBounce),
            C(100775, 115300, tiltHigh),
            C(115300, 120100, tiltInverseBounce),
            C(120100, 133950, tiltHigh),
            C(133950, 138750, tiltInverseBounce),
            C(138750, 153650, tiltHigh),
            C(153650, 158550, tiltFall),
            C(158550, 166000, tiltPark),
        ];

        var all100 = PatternFactory.Dimmer(DimmerPattern.All100, headCount);
        var all75 = PatternFactory.Dimmer(DimmerPattern.All75, headCount);
        var all70 = PatternFactory.Dimmer(DimmerPattern.All70, headCount);
        var all50 = PatternFactory.Dimmer(DimmerPattern.All50, headCount);
        var all45 = PatternFactory.Dimmer(DimmerPattern.All45, headCount);
        var outerHigh = PatternFactory.Dimmer(DimmerPattern.OuterHigh, headCount);
        var centerHigh = PatternFactory.Dimmer(DimmerPattern.CenterHigh, headCount);
        var fade75 = PatternFactory.Dimmer(DimmerPattern.Fade75, headCount);
        var fade30 = PatternFactory.Dimmer(DimmerPattern.Fade30, headCount);
        Cue[] dimmerCues =
        [
            C(9050, 10000, all100),
            C(10000, 12100, outerHigh),
            C(12100, 12375, all45),
            C(12375, 14525, centerHigh),
            C(14525, 14775, all45),
            C(14775, 18850, outerHigh),
            C(18850, 19275, all100),
            C(19525, 21675, centerHigh),
            C(21675, 21950, all45),
            C(21950, 24150, outerHigh),
            C(24150, 24350, all45),
            C(24350, 28425, centerHigh),
            C(28550, 28875, all100),
            C(28875, 47950, all75),
            C(52100, 52850, all100),
            C(52850, 53150, all50),
            C(53150, 55425, outerHigh),
            C(55425, 55575, all45),
            C(55575, 57725, centerHigh),
            C(57725, 57975, all45),
            C(57975, 60100, outerHigh),
            C(60100, 62150, centerHigh),
            C(62150, 62475, all100),
            C(62475, 62750, all45),
            C(62750, 64875, outerHigh),
            C(64875, 65200, all45),
            C(65200, 67325, centerHigh),
            C(67325, 67600, all45),
            C(67600, 71625, all75),
            C(72025, 79225, all75),
            C(88575, 91125, all100),
            C(91125, 94375, all75),
            C(94375, 95975, all100),
            C(95975, 105725, all70),
            C(115300, 124750, outerHigh),
            C(133950, 143750, all100),
            C(143750, 148550, all75),
            C(153650, 162300, fade75),
            C(162300, 166000, fade30),
        ];

        var shutterEvents = RootEffectEvent.FromDimmerCues(dimmerCues);

        var definitions = EffectDefinitions.CreateBase();
        definitions.AddOnHold("D30", 30);
        definitions.AddOnHold("D45", 45);
        definitions.AddOnHold("D50", 50);
        definitions.AddOnHold("D70", 70);
        definitions.AddOnHold("D75", 75);
        definitions.AddOnHold("D100", 100);
        definitions.AddOnRamp("D75_30", 75, 30);
        definitions.AddOnRamp("D30_0", 30, 0);
        definitions.AddCueDefinitions(panCues.Concat(tiltCues).Concat(dimmerCues));

        return new SequencePlan(
            SequenceKind.LilJon,
            "All I Really Want For Christmas (feat.xsq",
            $"All-I-Really-Want-For-Christmas-{headCount}MH.xsq",
            166000,
            0,
            definitions,
            panCues,
            tiltCues,
            dimmerCues,
            shutterEvents,
            PreserveLegacyDimmer: false);
    }
}