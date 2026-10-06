using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Sequences;

internal static class StormSequenceDefinition
{
    public static SequencePlan Create(int headCount, bool layoutDriven)
        => Create(headCount, layoutDriven, new FixtureProfileSettings());

    public static SequencePlan Create(int headCount, bool layoutDriven, FixtureProfileSettings fixtureProfile)
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
        var bounce = PatternFactory.Pan(PanPattern.Bounce, headCount);

        Cue[] panCues =
        [
            C(0, 27400, park),
            C(27400, 34275, openFan),
            C(34275, 41125, fan),
            C(41125, 51400, cross),
            C(51400, 63400, oppositeFan),
            C(63400, 65125, closeOppositeFan),
            C(65125, 78825, park),
            C(78825, 83975, openFan),
            C(83975, 90825, fan),
            C(90825, 92550, closeFan),
            C(92550, 106275, park),
            C(106275, 112675, openFan),
            C(112675, 119550, fan),
            C(119550, 126850, cross),
            C(126850, 141425, oppositeFan),
            C(141425, 147400, uncross),
            C(147400, 158550, fan),
            C(158550, 164950, closeFan),
            C(164950, 171400, park),
            C(171400, 177400, openFan),
            C(177400, 184700, fan),
            C(184700, 190250, cross),
            C(190250, 197100, oppositeFan),
            C(197100, 205225, closeOppositeFan),
            C(205225, 219125, park),
        ];

        var tiltPark = PatternFactory.Tilt(TiltPattern.Park, headCount, fixtureProfile);
        var tiltHigh = PatternFactory.Tilt(TiltPattern.High, headCount, fixtureProfile);
        var tiltRise = PatternFactory.Tilt(TiltPattern.Rise, headCount, fixtureProfile);
        var tiltFall = PatternFactory.Tilt(TiltPattern.Fall, headCount, fixtureProfile);
        var tiltInverseBounce = PatternFactory.Tilt(TiltPattern.InverseBounce, headCount, fixtureProfile);
        Cue[] tiltCues =
        [
            C(0, 27400, tiltPark),
            C(27400, 34275, tiltRise),
            C(34275, 41125, tiltHigh),
            C(41125, 51400, tiltInverseBounce),
            C(51400, 63400, tiltHigh),
            C(63400, 65125, tiltFall),
            C(65125, 78825, tiltPark),
            C(78825, 83975, tiltRise),
            C(83975, 90825, tiltHigh),
            C(90825, 92550, tiltFall),
            C(92550, 106275, tiltPark),
            C(106275, 112675, tiltRise),
            C(112675, 119550, tiltHigh),
            C(119550, 126850, tiltInverseBounce),
            C(126850, 141425, tiltHigh),
            C(141425, 147400, tiltInverseBounce),
            C(147400, 158550, tiltHigh),
            C(158550, 164950, tiltFall),
            C(164950, 171400, tiltPark),
            C(171400, 177400, tiltRise),
            C(177400, 184700, tiltHigh),
            C(184700, 190250, tiltInverseBounce),
            C(190250, 197100, tiltHigh),
            C(197100, 205225, tiltFall),
            C(205225, 219125, tiltPark),
        ];

        var dimmerCues = CreateDimmerCues(headCount, C);
        var shutterEvents = RootEffectEvent.FromDimmerCues(dimmerCues);

        var definitions = EffectDefinitions.CreateBase();
        definitions.AddCueDefinitions(panCues.Concat(tiltCues).Concat(dimmerCues));

        return new SequencePlan(
            SequenceKind.Storm,
            "A-Christmas-Storm(old_layout).xsq",
            $"A-Christmas-Storm(old_layout)-{headCount}MH.xsq",
            219125,
            380,
            definitions,
            panCues,
            tiltCues,
            dimmerCues,
            shutterEvents,
            PreserveLegacyDimmer: !layoutDriven && headCount == 4);
    }

    private static Cue[] CreateDimmerCues(
        int headCount,
        Func<int, int, IReadOnlyList<string>, Cue> create)
    {
        var all0 = PatternFactory.Dimmer(DimmerPattern.All0, headCount);
        var all45 = PatternFactory.Dimmer(DimmerPattern.All45, headCount);
        var all70 = PatternFactory.Dimmer(DimmerPattern.All70, headCount);
        var all75 = PatternFactory.Dimmer(DimmerPattern.All75, headCount);
        var all100 = PatternFactory.Dimmer(DimmerPattern.All100, headCount);
        var outerHigh = PatternFactory.Dimmer(DimmerPattern.OuterHigh, headCount);
        var centerHigh = PatternFactory.Dimmer(DimmerPattern.CenterHigh, headCount);
        var fade75 = PatternFactory.Dimmer(DimmerPattern.Fade75, headCount);
        var fade30 = PatternFactory.Dimmer(DimmerPattern.Fade30, headCount);

        return
        [
            create(0, 11625, all0),
            create(11625, 14100, outerHigh),
            create(14100, 18450, all0),
            create(18450, 20975, centerHigh),
            create(20975, 27400, all0),
            create(27400, 34275, outerHigh),
            create(34275, 39425, centerHigh),
            create(39425, 41125, all100),
            create(41125, 51400, all75),
            create(51400, 52275, all100),
            create(52275, 58675, all0),
            create(58675, 63400, all75),
            create(63400, 65125, all45),
            create(65125, 71125, all0),
            create(71125, 71975, all45),
            create(71975, 74550, outerHigh),
            create(74550, 75400, all45),
            create(75400, 77975, centerHigh),
            create(77975, 78825, all100),
            create(78825, 83975, outerHigh),
            create(83975, 90825, centerHigh),
            create(90825, 92550, all100),
            create(92550, 106275, all0),
            create(106275, 112675, all70),
            create(112675, 119550, outerHigh),
            create(119550, 126850, centerHigh),
            create(126850, 133700, all75),
            create(133700, 141425, all0),
            create(141425, 147400, outerHigh),
            create(147400, 153425, centerHigh),
            create(153425, 158550, all100),
            create(158550, 171400, all0),
            create(171400, 177400, outerHigh),
            create(177400, 184700, centerHigh),
            create(184700, 190250, outerHigh),
            create(190250, 197100, centerHigh),
            create(197100, 198825, all100),
            create(198825, 205225, fade75),
            create(205225, 211825, fade30),
            create(211825, 219125, all0),
        ];
    }
}