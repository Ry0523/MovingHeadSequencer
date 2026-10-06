using System.Text.RegularExpressions;
using System.Xml;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Timing;

namespace MovingHeadSequencer.Web;

internal static partial class CueRegenerationAdvisor
{
    private const int MinimumMovementDurationMs = 200;

    internal sealed record WholeSongAdvice(
        CueSheet CueSheet,
        IReadOnlyList<AppliedRegenerationChoice> AppliedChoices);

    public static CueRegenerationResult Suggest(
        string sourcePath,
        CueSheet cueSheet,
        int cueIndex,
        TimingMap timing,
        string? timingSourceName)
    {
        var sourceEffects = ReadConcurrentEffectIndex(sourcePath);
        return Suggest(cueSheet, cueIndex, timing, timingSourceName, sourceEffects);
    }

    public static WholeSongAdvice Regenerate(
        string sourcePath,
        CueSheet cueSheet,
        TimingMap timing,
        string? timingSourceName)
    {
        var sourceEffects = ReadConcurrentEffectIndex(sourcePath);
        var sections = cueSheet.Sections.Select(section => section with { }).ToArray();
        var applied = new List<AppliedRegenerationChoice>(sections.Length);
        for (var cueIndex = 0; cueIndex < sections.Length; cueIndex++)
        {
            var evolving = cueSheet with { Sections = sections };
            var suggestion = Suggest(evolving, cueIndex, timing, timingSourceName, sourceEffects);
            var choice = suggestion.Choices[0];
            sections[cueIndex] = Apply(sections[cueIndex], choice);
            applied.Add(new AppliedRegenerationChoice(
                cueIndex,
                choice.Id,
                choice.Name,
                choice.Energy,
                choice.Confidence,
                choice.Rationale,
                choice.Evidence,
                choice.Pan,
                choice.Tilt,
                choice.Dimmer,
                choice.Rhythm,
                choice.MotionEnergy,
                choice.MotionShape,
                choice.MotionPhase,
                choice.IntensityEnvelope,
                choice.ShutterOpen));
        }
        return new WholeSongAdvice(cueSheet with { Sections = sections }, applied);
    }

    private static CueRegenerationResult Suggest(
        CueSheet cueSheet,
        int cueIndex,
        TimingMap timing,
        string? timingSourceName,
        IReadOnlyList<SourceEffect> sourceEffects)
    {
        if (cueIndex < 0 || cueIndex >= cueSheet.Sections.Count)
        {
            throw new InvalidDataException(
                $"Cue index {cueIndex} is outside 0-{cueSheet.Sections.Count - 1}.");
        }

        var section = cueSheet.Sections[cueIndex];
        var midpoint = section.StartMs + ((section.EndMs - section.StartMs) / 2);
        var phrase = timing.Phrases
            .Where(marker => marker.StartMs <= midpoint && marker.EndMs > midpoint)
            .OrderBy(marker => marker.Kind == TimingTrackKind.Section ? 0 : 1)
            .ThenBy(marker => marker.EndMs - marker.StartMs)
            .FirstOrDefault();
        var contextLabel = phrase?.Label ?? section.Name;
        var beatSource = timing.BeatSources.FirstOrDefault(source =>
                source.TrackName.Equals(timingSourceName, StringComparison.OrdinalIgnoreCase))
            ?? timing.BeatSources.FirstOrDefault(source =>
                source.TrackName.Equals(timing.DefaultBeatSource, StringComparison.OrdinalIgnoreCase))
            ?? timing.BeatSources.FirstOrDefault();
        var beatCount = beatSource?.Beats.Count(beat =>
            beat.TimeMs >= section.StartMs && beat.TimeMs < section.EndMs) ?? 0;
        var concurrent = ReadConcurrentEffects(sourceEffects, section.StartMs, section.EndMs);
        var features = Classify(contextLabel, section, beatCount, concurrent);
        var choices = BuildChoices(
                cueSheet,
                cueIndex,
                features,
                beatSource?.Beats.Count >= 2)
            .OrderByDescending(choice => choice.Confidence)
            .ThenBy(choice => choice.Id, StringComparer.Ordinal)
            .ToArray();

        return new CueRegenerationResult(
            cueIndex,
            contextLabel,
            section.EndMs - section.StartMs,
            beatCount,
            concurrent,
            choices);
    }

    private static CueSheetSection Apply(
        CueSheetSection section,
        CueRegenerationChoice choice) => section with
    {
        Pattern = null,
        Pan = choice.Pan,
        Tilt = choice.Tilt,
        Dimmer = choice.Dimmer,
        Rhythm = choice.Rhythm,
        MotionEnergy = choice.MotionEnergy,
        MotionShape = choice.MotionShape,
        MotionPhase = choice.MotionPhase,
        IntensityEnvelope = choice.IntensityEnvelope,
        ShutterOpen = choice.ShutterOpen,
        PanKeys = null,
        TiltKeys = null,
        DimmerKeys = null,
    };

    private static IReadOnlyList<CueRegenerationChoice> BuildChoices(
        CueSheet cueSheet,
        int cueIndex,
        CueFeatures features,
        bool hasBeatTiming)
    {
        var section = cueSheet.Sections[cueIndex];
        var incoming = cueIndex > 0
            ? cueSheet.Resolve(cueSheet.Sections[cueIndex - 1])
            : cueSheet.Resolve(section);
        var outgoing = cueIndex + 1 < cueSheet.Sections.Count
            ? cueSheet.Resolve(cueSheet.Sections[cueIndex + 1])
            : incoming;
        var incomingPan = incoming.Pan ?? PanPattern.Park;
        var incomingTilt = incoming.Tilt ?? TiltPattern.Park;
        var transitionPan = TransitionPan(
            incomingPan,
            outgoing.Pan ?? incoming.Pan ?? PanPattern.Park);
        var transitionTilt = TransitionTilt(
            incomingTilt,
            outgoing.Tilt ?? incoming.Tilt ?? TiltPattern.Park);
        if (section.EndMs - section.StartMs < MinimumMovementDurationMs)
        {
            transitionPan = StablePan(incomingPan);
            transitionTilt = StableTilt(incomingTilt);
        }
        var highAtBothBoundaries = StableTilt(incomingTilt) == TiltPattern.High &&
            StableTilt(outgoing.Tilt ?? incomingTilt) == TiltPattern.High;
        var parkAtBothBoundaries = StableTilt(incomingTilt) == TiltPattern.Park &&
            StableTilt(outgoing.Tilt ?? incomingTilt) == TiltPattern.Park;
        var stableIncomingPan = StablePan(incomingPan);
        var stableOutgoingPan = StablePan(outgoing.Pan ?? incomingPan);
        var panRhythmAtBothBoundaries = stableIncomingPan == stableOutgoingPan &&
            stableIncomingPan is PanPattern.Fan or PanPattern.OppositeFan;
        var staticAtBothBoundaries = incoming.Pan == outgoing.Pan && incoming.Tilt == outgoing.Tilt;
        var selectedRhythm = SelectRhythmPattern(
            cueSheet,
            cueIndex,
            features,
            hasBeatTiming,
            highAtBothBoundaries,
            parkAtBothBoundaries,
            panRhythmAtBothBoundaries);
        var selectedEnergy = SelectMotionEnergy(features, selectedRhythm, cueIndex);
        var selectedShape = SelectMotionShape(features, selectedRhythm, cueIndex);
        var selectedPhase = SelectMotionPhase(features, selectedRhythm, cueIndex);
        var selectedEnvelope = SelectIntensityEnvelope(features, selectedRhythm, cueIndex);
        var evidence = BaseEvidence(features);
        var choices = new List<CueRegenerationChoice>
        {
            Create(
                "rest",
                staticAtBothBoundaries ? "Rest / Blackout" : "Dark Transition",
                "Rest",
                features.Quiet ? 92 : features.Energetic ? 48 : 68,
                features.Quiet
                    ? "Preserves contrast while moving safely toward the following pose in darkness."
                    : "Creates a deliberate motion rest and hides the transition into the following pose.",
                [.. evidence, "Storm: blackout-gated repositioning", "Boundary-matched transition"],
                transitionPan,
                transitionTilt,
                DimmerPattern.All0,
                RhythmPattern.Off,
                false,
                MotionEnergy.Medium,
                MotionShape.Smooth,
                MotionPhase.Together,
                IntensityEnvelope.Steady),
            Create(
                "hold",
                staticAtBothBoundaries ? "Static Symmetric Hold" : "Restrained Transition",
                "Restrained",
                features.Quiet ? 88 : features.Energetic ? 67 : 82,
                staticAtBothBoundaries
                    ? "Keeps the incoming pose while intensity carries the cue detail."
                    : "Uses one quiet transition between neighboring poses without rhythmic movement.",
                [.. evidence, "Where Are You Christmas: phrase holds", "Boundary-matched transition"],
                transitionPan,
                transitionTilt,
                DimmerPattern.All100,
                RhythmPattern.Off,
                true,
                MotionEnergy.Subtle,
                MotionShape.Smooth,
                MotionPhase.Together,
                features.Quiet ? IntensityEnvelope.FadeOut : IntensityEnvelope.Steady),
        };

        if (features.Build || features.Vertical)
        {
            choices.Add(Create(
                "phrase",
                "Vertical Reveal",
                "Balanced",
                features.Build ? 93 : 84,
                "Uses one coordinated rise across the cue instead of spending motion on every beat.",
                [.. evidence, "Firework: tilt-only launch", "Lil Jon: open-fan reveal"],
                transitionPan,
                highAtBothBoundaries ? TiltPattern.InverseBounce : transitionTilt,
                DimmerPattern.All100,
                RhythmPattern.Off,
                true,
                MotionEnergy.Medium,
                MotionShape.Smooth,
                features.Spatial ? MotionPhase.CenterOut : MotionPhase.Together,
                IntensityEnvelope.FadeIn));
        }
        else
        {
            choices.Add(Create(
                "phrase",
                features.Spatial ? "Mirrored Phrase Cross" : "Phrase Sweep",
                "Balanced",
                features.Spatial ? 90 : features.Quiet ? 70 : 84,
                features.Spatial
                    ? "Mirrors the concurrent center-out or radial display motion over one phrase."
                    : "Provides one continuous movement across the cue with a stable arrival pose.",
                [.. evidence, features.Spatial
                    ? "Rave / Bloody Mary: mirrored ramps"
                    : "Where Are You Christmas: long phrase motion"],
                transitionPan,
                features.Spatial && highAtBothBoundaries
                    ? TiltPattern.InverseBounce
                    : transitionTilt,
                DimmerPattern.All100,
                RhythmPattern.Off,
                true,
                features.Quiet ? MotionEnergy.Subtle : MotionEnergy.Medium,
                MotionShape.Smooth,
                features.Spatial ? MotionPhase.CenterOut : MotionPhase.Together,
                features.Quiet ? IntensityEnvelope.FadeOut : IntensityEnvelope.FadeIn));
        }

        if (selectedRhythm != RhythmPattern.Off)
        {
            choices.Add(Create(
                "energy",
                RhythmChoiceName(selectedRhythm, features.Accent),
                "Energetic",
                features.Accent ? 94 : 91,
                RhythmRationale(selectedRhythm, features.Accent),
                [.. evidence, RhythmEvidence(selectedRhythm)],
                UsesPanRhythm(selectedRhythm) ? stableIncomingPan : transitionPan,
                UsesTiltRhythm(selectedRhythm)
                    ? RhythmTiltAnchor(selectedRhythm)
                    : transitionTilt,
                RhythmDimmer(selectedRhythm, features.Accent),
                selectedRhythm,
                true,
                selectedEnergy,
                selectedShape,
                selectedPhase,
                selectedEnvelope));
        }
        else if (features.Accent)
        {
            choices.Add(Create(
                "energy",
                "Stable Intensity Hit",
                "Energetic",
                94,
                "A short cue is cleaner as an intensity accent than as a mechanical position snap.",
                [.. evidence, "Catalog: accent hits keep position stable"],
                transitionPan,
                transitionTilt,
                DimmerPattern.All100,
                RhythmPattern.Off,
                true,
                MotionEnergy.Medium,
                MotionShape.Punch,
                MotionPhase.Together,
                selectedEnvelope));
        }
        else
        {
            choices.Add(Create(
                "energy",
                "Measured Energy Sweep",
                "Energetic",
                features.Rhythmic || features.Energetic ? 91 : features.Quiet ? 58 : 78,
                "Adds one measured phrase movement while avoiding continuous mechanical motion.",
                [.. evidence, "Bloody Mary: compact movement vocabulary"],
                transitionPan,
                transitionTilt,
                DimmerPattern.All100,
                RhythmPattern.Off,
                true,
                features.Energetic ? MotionEnergy.Full : MotionEnergy.Medium,
                features.Accent ? MotionShape.Punch : MotionShape.Smooth,
                features.Spatial ? MotionPhase.LeftToRight : MotionPhase.Together,
                selectedEnvelope));
        }

        var contrastRest = ShouldCreateContrastRest(cueSheet, cueIndex, features);
        return choices
            .Select(choice => NormalizeShortCue(choice, section, incomingPan, incomingTilt))
            .Select(choice => ApplyNeighborScoring(choice, cueSheet, cueIndex, section))
            .Select(choice => contrastRest
                ? choice with
                {
                    Confidence = choice.Id == "rest" ? 96 : Math.Min(choice.Confidence, 88),
                    Evidence = choice.Id == "rest"
                        ? [.. choice.Evidence, "Contrast policy: intentional blackout window"]
                        : choice.Evidence,
                }
                : choice)
            .ToArray();
    }

    private static bool ShouldCreateContrastRest(
        CueSheet cueSheet,
        int cueIndex,
        CueFeatures features)
    {
        var section = cueSheet.Sections[cueIndex];
        var duration = section.EndMs - section.StartMs;
        if (cueIndex == 0 && duration >= 4000)
        {
            return true;
        }
        if (cueIndex == cueSheet.Sections.Count - 1 && duration >= 1500)
        {
            return true;
        }

        var unlabeledTransition = features.ContextLabel.Equals(
                section.Name,
                StringComparison.OrdinalIgnoreCase) &&
            section.Name.StartsWith("Cue ", StringComparison.OrdinalIgnoreCase);
        if (!unlabeledTransition || duration < 4000)
        {
            return false;
        }

        var lastDarkEnd = cueSheet.Sections
            .Take(cueIndex)
            .Where(candidate =>
            {
                var pattern = cueSheet.Resolve(candidate);
                return pattern.ShutterOpen == false || pattern.Dimmer == DimmerPattern.All0;
            })
            .Select(candidate => candidate.EndMs)
            .DefaultIfEmpty(-20000)
            .Max();
        return section.StartMs - lastDarkEnd >= 20000;
    }

    private static RhythmPattern SelectRhythmPattern(
        CueSheet cueSheet,
        int cueIndex,
        CueFeatures features,
        bool hasBeatTiming,
        bool highAtBothBoundaries,
        bool parkAtBothBoundaries,
        bool panRhythmAtBothBoundaries)
    {
        if (!hasBeatTiming || !features.Rhythmic ||
            (!highAtBothBoundaries && !parkAtBothBoundaries))
        {
            return RhythmPattern.Off;
        }

        var previousRhythm = cueIndex > 0
            ? cueSheet.Resolve(cueSheet.Sections[cueIndex - 1]).Rhythm ?? RhythmPattern.Off
            : RhythmPattern.Off;
        var consecutivePrevious = 0;
        for (var index = cueIndex - 1; index >= 0; index--)
        {
            if ((cueSheet.Resolve(cueSheet.Sections[index]).Rhythm ?? RhythmPattern.Off) !=
                previousRhythm)
            {
                break;
            }
            consecutivePrevious++;
        }
        if (parkAtBothBoundaries)
        {
            return previousRhythm == RhythmPattern.TiltLiftEveryBeat &&
                consecutivePrevious >= 2
                    ? RhythmPattern.Off
                    : RhythmPattern.TiltLiftEveryBeat;
        }
        var priorOrbit = cueSheet.Sections
            .Take(cueIndex)
            .Any(candidate => (cueSheet.Resolve(candidate).Rhythm ?? RhythmPattern.Off) ==
                RhythmPattern.PanTiltBounceEveryFourBeats);
        if (features.Spatial && highAtBothBoundaries && panRhythmAtBothBoundaries && !priorOrbit)
        {
            return RhythmPattern.PanTiltBounceEveryFourBeats;
        }
        if (previousRhythm != RhythmPattern.Off &&
            consecutivePrevious < 2 &&
            RhythmFitsBoundaries(
                previousRhythm,
                highAtBothBoundaries,
                parkAtBothBoundaries,
                panRhythmAtBothBoundaries))
        {
            return previousRhythm;
        }

        var effects = string.Join(' ', features.ConcurrentEffects).ToLowerInvariant();
        RhythmPattern[] candidates = features.Spatial
            ?
            [
                RhythmPattern.TiltPairChaseEveryBeat,
                RhythmPattern.PanTiltBounceEveryFourBeats,
                RhythmPattern.PanBounceEveryTwoBeats,
                RhythmPattern.TiltSyncopatedPulse,
                RhythmPattern.TiltHeadChaseEveryBeat,
                RhythmPattern.TiltCenterOuterEveryBeat,
                RhythmPattern.TiltOddEvenEveryBeat,
            ]
            : ContainsAny(effects, "bars", "single strand", "singlestrand", "chase", "lines")
                ?
                [
                    RhythmPattern.TiltHeadChaseEveryBeat,
                    RhythmPattern.TiltPairChaseEveryBeat,
                    RhythmPattern.PanBounceEveryTwoBeats,
                    RhythmPattern.TiltSyncopatedPulse,
                    RhythmPattern.TiltCenterOuterEveryBeat,
                    RhythmPattern.TiltOddEvenEveryBeat,
                    RhythmPattern.TiltBounceEveryBeat,
                ]
                : effects.Contains("vu meter", StringComparison.Ordinal)
                    ?
                    [
                        RhythmPattern.TiltPairChaseEveryBeat,
                        RhythmPattern.TiltHeadChaseEveryBeat,
                        RhythmPattern.TiltPulseEveryHalfBeat,
                        RhythmPattern.TiltOddEvenEveryBeat,
                        RhythmPattern.TiltCenterOuterEveryBeat,
                        RhythmPattern.TiltBounceEveryBeat,
                        RhythmPattern.TiltBounceEveryTwoBeats,
                    ]
                    :
                    [
                        RhythmPattern.TiltHeadChaseEveryBeat,
                        RhythmPattern.TiltPairChaseEveryBeat,
                        RhythmPattern.TiltSyncopatedPulse,
                        RhythmPattern.TiltPulseEveryHalfBeat,
                        RhythmPattern.TiltBounceEveryBeat,
                        RhythmPattern.TiltCenterOuterEveryBeat,
                        RhythmPattern.TiltOddEvenEveryBeat,
                        RhythmPattern.TiltBounceEveryTwoBeats,
                    ];
        var offset = (cueIndex / 2) % candidates.Length;
        return Enumerable.Range(0, candidates.Length)
            .Select(index => candidates[(offset + index) % candidates.Length])
            .Where(pattern => consecutivePrevious < 2 || pattern != previousRhythm)
            .First(pattern => RhythmFitsBoundaries(
                pattern,
                highAtBothBoundaries,
                parkAtBothBoundaries,
                panRhythmAtBothBoundaries));
    }

    private static bool RhythmFitsBoundaries(
        RhythmPattern rhythm,
        bool highAtBothBoundaries,
        bool parkAtBothBoundaries,
        bool panRhythmAtBothBoundaries) =>
        (rhythm == RhythmPattern.TiltLiftEveryBeat
            ? parkAtBothBoundaries
            : !UsesTiltRhythm(rhythm) || highAtBothBoundaries) &&
        (!UsesPanRhythm(rhythm) || panRhythmAtBothBoundaries);

    private static bool UsesPanRhythm(RhythmPattern rhythm) => rhythm is
        RhythmPattern.PanBounceEveryTwoBeats or RhythmPattern.PanTiltBounceEveryFourBeats;

    private static bool UsesTiltRhythm(RhythmPattern rhythm) => rhythm is
        RhythmPattern.TiltLiftEveryBeat or
        RhythmPattern.TiltPulseEveryHalfBeat or
        RhythmPattern.TiltBounceEveryBeat or
        RhythmPattern.TiltBounceEveryTwoBeats or
        RhythmPattern.TiltSyncopatedPulse or
        RhythmPattern.TiltOddEvenEveryBeat or
        RhythmPattern.TiltCenterOuterEveryBeat or
        RhythmPattern.TiltHeadChaseEveryBeat or
        RhythmPattern.TiltPairChaseEveryBeat or
        RhythmPattern.PanTiltBounceEveryFourBeats;

    private static string RhythmChoiceName(RhythmPattern rhythm, bool accent)
    {
        var name = rhythm switch
        {
            RhythmPattern.TiltLiftEveryBeat => "Beat-Pulse Lift",
            RhythmPattern.TiltPulseEveryHalfBeat => "Half-Beat Tilt Punch",
            RhythmPattern.TiltBounceEveryBeat => "Beat-Pulse Tilt",
            RhythmPattern.TiltBounceEveryTwoBeats => "Two-Beat Tilt Bounce",
            RhythmPattern.TiltSyncopatedPulse => "Syncopated Tilt Pulse",
            RhythmPattern.TiltOddEvenEveryBeat => "Odd / Even Tilt Chase",
            RhythmPattern.TiltCenterOuterEveryBeat => "Center / Outer Tilt Chase",
            RhythmPattern.TiltHeadChaseEveryBeat => "One-by-One Tilt Chase",
            RhythmPattern.TiltPairChaseEveryBeat => "Mirrored Pair Tilt Chase",
            RhythmPattern.PanBounceEveryTwoBeats => "Two-Beat Pan Sweep",
            RhythmPattern.PanTiltBounceEveryFourBeats => "Four-Beat Pan / Tilt Orbit",
            _ => throw new ArgumentOutOfRangeException(nameof(rhythm)),
        };
        return accent ? $"{name} Accent" : name;
    }

    private static string RhythmRationale(RhythmPattern rhythm, bool accent)
    {
        var movement = rhythm switch
        {
            RhythmPattern.TiltLiftEveryBeat =>
                "Lifts the parked bank on each beat and returns to the low pose.",
            RhythmPattern.TiltPulseEveryHalfBeat =>
                "Adds compact half-beat vertical punctuation without changing the stable pose.",
            RhythmPattern.TiltBounceEveryBeat => "Pulses the full bank vertically on each beat.",
            RhythmPattern.TiltBounceEveryTwoBeats => "Uses a measured two-beat dip and return.",
            RhythmPattern.TiltSyncopatedPulse =>
                "Alternates short and long intervals to place movement between the main beats.",
            RhythmPattern.TiltOddEvenEveryBeat =>
                "Alternates odd and even fixtures each beat for a spatial chase.",
            RhythmPattern.TiltCenterOuterEveryBeat =>
                "Trades each beat between the outer fixtures and the center bank.",
            RhythmPattern.TiltHeadChaseEveryBeat =>
                "Advances a single moving-head dip across the physical fixture line each beat.",
            RhythmPattern.TiltPairChaseEveryBeat =>
                "Advances mirrored fixture pairs from the outside toward the center each beat.",
            RhythmPattern.PanBounceEveryTwoBeats =>
                "Sweeps the fan across and back over two beats.",
            RhythmPattern.PanTiltBounceEveryFourBeats =>
                "Combines pan and tilt over four beats for broad diagonal motion.",
            _ => throw new ArgumentOutOfRangeException(nameof(rhythm)),
        };
        return accent
            ? $"{movement} Intensity carries the short accent without restarting the run."
            : movement;
    }

    private static string RhythmEvidence(RhythmPattern rhythm) => rhythm switch
    {
        RhythmPattern.TiltLiftEveryBeat => "Firework / Your Idol: beat-sized tilt lift",
        RhythmPattern.TiltPulseEveryHalfBeat => "Lil Jon fills: half-beat punctuation",
        RhythmPattern.TiltBounceEveryBeat => "Your Idol: beat-sized alternating tilt ramps",
        RhythmPattern.TiltBounceEveryTwoBeats => "Uptown Funk: sustained beat articulation",
        RhythmPattern.TiltSyncopatedPulse => "Magic / Uptown Funk: off-beat movement accents",
        RhythmPattern.TiltOddEvenEveryBeat => "Opalite: odd/even fixture paths",
        RhythmPattern.TiltCenterOuterEveryBeat => "Catalog: center-out / outside-in grouping",
        RhythmPattern.TiltHeadChaseEveryBeat => "Catalog: left-to-right per-head chase",
        RhythmPattern.TiltPairChaseEveryBeat => "Demon Hunter: mirrored subgroup isolation",
        RhythmPattern.PanBounceEveryTwoBeats => "Sounding Joy: repeating pan oscillator",
        RhythmPattern.PanTiltBounceEveryFourBeats => "Rave: combined pan/tilt cycles",
        _ => throw new ArgumentOutOfRangeException(nameof(rhythm)),
    };

    private static DimmerPattern RhythmDimmer(RhythmPattern rhythm, bool accent) =>
        DimmerPattern.All100;

    private static TiltPattern RhythmTiltAnchor(RhythmPattern rhythm) =>
        rhythm == RhythmPattern.TiltLiftEveryBeat
            ? TiltPattern.Park
            : TiltPattern.High;

    private static MotionEnergy SelectMotionEnergy(
        CueFeatures features,
        RhythmPattern rhythm,
        int cueIndex)
    {
        if (features.Quiet)
        {
            return MotionEnergy.Subtle;
        }
        if (features.Build)
        {
            return MotionEnergy.Medium;
        }
        var supportsExtended = rhythm is RhythmPattern.TiltBounceEveryTwoBeats or
            RhythmPattern.PanBounceEveryTwoBeats or
            RhythmPattern.PanTiltBounceEveryFourBeats;
        if (features.Energetic && supportsExtended && features.DurationMs >= 1800)
        {
            return MotionEnergy.Extended;
        }
        return features.Energetic ? MotionEnergy.Full : MotionEnergy.Medium;
    }

    private static MotionShape SelectMotionShape(
        CueFeatures features,
        RhythmPattern rhythm,
        int cueIndex)
    {
        if (rhythm == RhythmPattern.Off)
        {
            return features.Accent ? MotionShape.Punch : MotionShape.Smooth;
        }
        if (features.Accent || rhythm is RhythmPattern.TiltPulseEveryHalfBeat or
            RhythmPattern.TiltSyncopatedPulse)
        {
            return MotionShape.Punch;
        }
        var supportsDouble = rhythm is RhythmPattern.TiltBounceEveryTwoBeats or
            RhythmPattern.PanBounceEveryTwoBeats or
            RhythmPattern.PanTiltBounceEveryFourBeats;
        return supportsDouble && features.DurationMs >= 2400
            ? MotionShape.Double
            : MotionShape.Smooth;
    }

    private static MotionPhase SelectMotionPhase(
        CueFeatures features,
        RhythmPattern rhythm,
        int cueIndex)
    {
        if (!features.Spatial || rhythm is RhythmPattern.Off or
            RhythmPattern.TiltHeadChaseEveryBeat or RhythmPattern.TiltPairChaseEveryBeat or
            RhythmPattern.TiltOddEvenEveryBeat or RhythmPattern.TiltCenterOuterEveryBeat)
        {
            return MotionPhase.Together;
        }
        return (cueIndex % 3) switch
        {
            0 => MotionPhase.LeftToRight,
            1 => MotionPhase.CenterOut,
            _ => MotionPhase.AlternatingPairs,
        };
    }

    private static IntensityEnvelope SelectIntensityEnvelope(
        CueFeatures features,
        RhythmPattern rhythm,
        int cueIndex)
    {
        if (features.Accent)
        {
            return cueIndex % 4 == 0
                ? IntensityEnvelope.Flash
                : IntensityEnvelope.RiseAndHold;
        }
        if (rhythm == RhythmPattern.TiltSyncopatedPulse ||
            rhythm == RhythmPattern.PanTiltBounceEveryFourBeats)
        {
            return IntensityEnvelope.TexturedPulse;
        }
        if (rhythm == RhythmPattern.TiltPulseEveryHalfBeat)
        {
            return IntensityEnvelope.Flash;
        }
        if (rhythm != RhythmPattern.Off)
        {
            return IntensityEnvelope.Pulse;
        }
        if (features.Build)
        {
            return IntensityEnvelope.FadeIn;
        }
        if (features.Quiet)
        {
            return IntensityEnvelope.FadeOut;
        }
        return features.Energetic
            ? IntensityEnvelope.RiseAndHold
            : IntensityEnvelope.Steady;
    }

    private static CueRegenerationChoice NormalizeShortCue(
        CueRegenerationChoice choice,
        CueSheetSection section,
        PanPattern incomingPan,
        TiltPattern incomingTilt)
    {
        if (section.EndMs - section.StartMs >= MinimumMovementDurationMs)
        {
            return choice;
        }
        if (choice.Rhythm != RhythmPattern.Off)
        {
            return choice with
            {
                Pan = StablePan(incomingPan),
                Tilt = choice.Tilt,
                Evidence = [.. choice.Evidence, "Short accent: continuous rhythm carries through safely"],
            };
        }
        return choice with
        {
            Pan = StablePan(incomingPan),
            Tilt = StableTilt(incomingTilt),
            Rhythm = RhythmPattern.Off,
            Evidence = [.. choice.Evidence, "Short cue: position held for mechanical safety"],
        };
    }

    private static PanPattern TransitionPan(PanPattern incoming, PanPattern outgoing)
    {
        if (incoming == outgoing)
        {
            return StablePan(incoming);
        }
        return (StablePan(incoming), StablePan(outgoing)) switch
        {
            (PanPattern.Park, PanPattern.Fan) => PanPattern.OpenFan,
            (PanPattern.Fan, PanPattern.Park) => PanPattern.CloseFan,
            (PanPattern.OppositeFan, PanPattern.Park) => PanPattern.CloseOppositeFan,
            (PanPattern.Fan, PanPattern.OppositeFan) => PanPattern.Cross,
            (PanPattern.OppositeFan, PanPattern.Fan) => PanPattern.Uncross,
            _ => StablePan(incoming),
        };
    }

    private static PanPattern StablePan(PanPattern pattern) => pattern switch
    {
        PanPattern.OpenFan or PanPattern.Uncross => PanPattern.Fan,
        PanPattern.CloseFan or PanPattern.CloseOppositeFan => PanPattern.Park,
        PanPattern.Cross => PanPattern.OppositeFan,
        PanPattern.Bounce => PanPattern.Fan,
        _ => pattern,
    };

    private static TiltPattern TransitionTilt(TiltPattern incoming, TiltPattern outgoing)
    {
        var first = StableTilt(incoming);
        var last = StableTilt(outgoing);
        if (first == last)
        {
            return first;
        }
        return (first, last) switch
        {
            (TiltPattern.Park, TiltPattern.High) => TiltPattern.Rise,
            (TiltPattern.High, TiltPattern.Park) => TiltPattern.Fall,
            _ => first,
        };
    }

    private static TiltPattern StableTilt(TiltPattern pattern) => pattern switch
    {
        TiltPattern.Rise or TiltPattern.InverseBounce => TiltPattern.High,
        TiltPattern.Fall or TiltPattern.Bounce => TiltPattern.Park,
        _ => pattern,
    };

    private static CueRegenerationChoice ApplyNeighborScoring(
        CueRegenerationChoice choice,
        CueSheet cueSheet,
        int cueIndex,
        CueSheetSection section)
    {
        var confidence = choice.Confidence;
        var evidence = choice.Evidence.ToList();
        if (cueIndex > 0)
        {
            var previous = cueSheet.Resolve(cueSheet.Sections[cueIndex - 1]);
            if (previous.Pan == choice.Pan && previous.Tilt == choice.Tilt)
            {
                confidence += 4;
                evidence.Add("Matches the preceding pose");
            }
        }
        if (cueIndex + 1 < cueSheet.Sections.Count)
        {
            var next = cueSheet.Resolve(cueSheet.Sections[cueIndex + 1]);
            if (next.Pan == choice.Pan && next.Tilt == choice.Tilt)
            {
                confidence += 3;
                evidence.Add("Arrives at the following pose");
            }
        }

        var repeated = Enumerable.Range(Math.Max(0, cueIndex - 2), Math.Min(2, cueIndex))
            .Select(index => cueSheet.Resolve(cueSheet.Sections[index]))
            .Count(pattern => pattern.Pan == choice.Pan && pattern.Tilt == choice.Tilt);
        if (repeated > 0 && choice.Id != "rest")
        {
            confidence -= repeated * 6;
            evidence.Add("Repetition penalty applied");
        }
        if (!choice.ShutterOpen && section.ShutterOpen == false)
        {
            confidence += 3;
        }

        return choice with
        {
            Confidence = Math.Clamp(confidence, 35, 96),
            Evidence = evidence,
        };
    }

    private static CueRegenerationChoice Create(
        string id,
        string name,
        string energy,
        int confidence,
        string rationale,
        IReadOnlyList<string> evidence,
        PanPattern pan,
        TiltPattern tilt,
        DimmerPattern dimmer,
        RhythmPattern rhythm,
        bool shutterOpen,
        MotionEnergy motionEnergy = MotionEnergy.Full,
        MotionShape motionShape = MotionShape.Smooth,
        MotionPhase motionPhase = MotionPhase.Together,
        IntensityEnvelope intensityEnvelope = IntensityEnvelope.Steady) => new(
            id,
            name,
            energy,
            confidence,
            rationale,
            evidence,
            pan,
            tilt,
            dimmer,
            rhythm,
            motionEnergy,
            motionShape,
            motionPhase,
            intensityEnvelope,
            shutterOpen);

    private static CueFeatures Classify(
        string contextLabel,
        CueSheetSection section,
        int beatCount,
        IReadOnlyList<string> concurrentEffects)
    {
        var text = $"{contextLabel} {section.Name}".ToLowerInvariant();
        var effects = string.Join(' ', concurrentEffects).ToLowerInvariant();
        var duration = section.EndMs - section.StartMs;
        var quiet = ContainsAny(text, "intro", "verse", "bridge", "break", "quiet", "outro") ||
            effects.Contains("off", StringComparison.Ordinal);
        var build = ContainsAny(text, "build", "rise", "riser", "lift") ||
            effects.Contains("firework", StringComparison.Ordinal);
        var energetic = ContainsAny(text, "chorus", "drop", "dance", "finale", "party") ||
            ContainsAny(effects, "pinwheel", "shockwave", "butterfly", "ripple", "bars");
        var accent = duration <= 900 || beatCount <= 1 ||
            ContainsAny(text, "yeah", "hey", "hit", "impact", "oh");
        var rhythmic = ContainsAny(effects,
            "vu meter", "single strand", "singlestrand", "marquee", "bars", "shimmer", "chase");
        var spatial = ContainsAny(effects,
            "pinwheel", "shockwave", "butterfly", "ripple", "spirals", "fan");
        var vertical = effects.Contains("firework", StringComparison.Ordinal);
        return new CueFeatures(
            contextLabel,
            duration,
            beatCount,
            quiet,
            build,
            energetic,
            accent,
            rhythmic,
            spatial,
            vertical,
            concurrentEffects);
    }

    private static string[] BaseEvidence(CueFeatures features)
    {
        var evidence = new List<string>
        {
            $"Context: {features.ContextLabel}",
            $"{features.DurationMs} ms / {features.BeatCount} beats",
        };
        if (features.ConcurrentEffects.Count > 0)
        {
            evidence.Add($"Concurrent: {string.Join(", ", features.ConcurrentEffects.Take(3))}");
        }
        return [.. evidence];
    }

    private static SourceEffect[] ReadConcurrentEffectIndex(string path)
    {
        var document = new XmlDocument();
        document.Load(path);
        return document.SelectNodes("/xsequence/ElementEffects/Element[@type='model']")
            ?.Cast<XmlElement>()
            .Where(model => !MovingHeadNameRegex().IsMatch(model.GetAttribute("name")))
            .SelectMany(model => model.SelectNodes(".//Effect")?.Cast<XmlElement>() ?? [])
            .Select(effect => new SourceEffect(
                effect.GetAttribute("name").Trim(),
                int.TryParse(effect.GetAttribute("startTime"), out var start) ? start : -1,
                int.TryParse(effect.GetAttribute("endTime"), out var end) ? end : -1))
            .Where(effect => effect.Name.Length > 0 &&
                !IgnoredEffectNames.Contains(effect.Name) && effect.End > effect.Start)
            .ToArray() ?? [];
    }

    private static string[] ReadConcurrentEffects(
        IReadOnlyList<SourceEffect> effects,
        int startMs,
        int endMs) => effects
            .Where(effect => effect.Start < endMs && effect.End > startMs)
            .Select(effect => effect.Name)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(group => group.Key)
            .ToArray();

    private static readonly HashSet<string> IgnoredEffectNames = new(
        ["DMX", "On", "Off"],
        StringComparer.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => term.Length <= 3
            ? Regex.IsMatch(
                value,
                $@"(?<![a-z0-9]){Regex.Escape(term)}(?![a-z0-9])",
                RegexOptions.CultureInvariant)
            : value.Contains(term, StringComparison.Ordinal));

    private sealed record CueFeatures(
        string ContextLabel,
        int DurationMs,
        int BeatCount,
        bool Quiet,
        bool Build,
        bool Energetic,
        bool Accent,
        bool Rhythmic,
        bool Spatial,
        bool Vertical,
        IReadOnlyList<string> ConcurrentEffects);

    private sealed record SourceEffect(string Name, int Start, int End);

    [GeneratedRegex(
        "moving head|(^|[^a-z])mh([^a-z]|$)|mover|dmxmovinghead|dmx head|lempa",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovingHeadNameRegex();
}
