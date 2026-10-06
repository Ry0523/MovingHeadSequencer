using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Sequences;

namespace MovingHeadSequencer.Choreography;

internal sealed record CuePatternDefinition
{
    [JsonConverter(typeof(JsonStringEnumConverter<PanPattern>))]
    public PanPattern? Pan { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TiltPattern>))]
    public TiltPattern? Tilt { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<DimmerPattern>))]
    public DimmerPattern? Dimmer { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<RhythmPattern>))]
    public RhythmPattern? Rhythm { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<MotionEnergy>))]
    public MotionEnergy? MotionEnergy { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<MotionShape>))]
    public MotionShape? MotionShape { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<MotionPhase>))]
    public MotionPhase? MotionPhase { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<IntensityEnvelope>))]
    public IntensityEnvelope? IntensityEnvelope { get; init; }

    public bool? ShutterOpen { get; init; }

    public CuePatternDefinition Overlay(CuePatternDefinition? overlay) => overlay is null
        ? this
        : new CuePatternDefinition
        {
            Pan = overlay.Pan ?? Pan,
            Tilt = overlay.Tilt ?? Tilt,
            Dimmer = overlay.Dimmer ?? Dimmer,
            Rhythm = overlay.Rhythm ?? Rhythm,
            MotionEnergy = overlay.MotionEnergy ?? MotionEnergy,
            MotionShape = overlay.MotionShape ?? MotionShape,
            MotionPhase = overlay.MotionPhase ?? MotionPhase,
            IntensityEnvelope = overlay.IntensityEnvelope ?? IntensityEnvelope,
            ShutterOpen = overlay.ShutterOpen ?? ShutterOpen,
        };
}

internal sealed record CueSheetSection
{
    public required string Name { get; init; }
    public int StartMs { get; init; }
    public int EndMs { get; init; }
    public string? Pattern { get; init; }
    public IReadOnlyList<int> Heads { get; init; } = [];
    public IReadOnlyList<string>? PanKeys { get; init; }
    public IReadOnlyList<string>? TiltKeys { get; init; }
    public IReadOnlyList<string>? DimmerKeys { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<PanPattern>))]
    public PanPattern? Pan { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TiltPattern>))]
    public TiltPattern? Tilt { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<DimmerPattern>))]
    public DimmerPattern? Dimmer { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<RhythmPattern>))]
    public RhythmPattern? Rhythm { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<MotionEnergy>))]
    public MotionEnergy? MotionEnergy { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<MotionShape>))]
    public MotionShape? MotionShape { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<MotionPhase>))]
    public MotionPhase? MotionPhase { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<IntensityEnvelope>))]
    public IntensityEnvelope? IntensityEnvelope { get; init; }

    public bool? ShutterOpen { get; init; }

    public CuePatternDefinition Overrides => new()
    {
        Pan = Pan,
        Tilt = Tilt,
        Dimmer = Dimmer,
        Rhythm = Rhythm,
        MotionEnergy = MotionEnergy,
        MotionShape = MotionShape,
        MotionPhase = MotionPhase,
        IntensityEnvelope = IntensityEnvelope,
        ShutterOpen = ShutterOpen,
    };
}

internal sealed record CueSheet
{
    public required string Name { get; init; }
    public int DurationMs { get; init; }
    public CuePatternDefinition Defaults { get; init; } = new();
    public IReadOnlyDictionary<string, CuePatternDefinition> Patterns { get; init; } =
        new Dictionary<string, CuePatternDefinition>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CueSheetSection> Sections { get; init; } = [];

    public static CueSheet Load(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Cue sheet was not found: {resolvedPath}", resolvedPath);
        }
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var sheet = JsonSerializer.Deserialize<CueSheet>(File.ReadAllText(resolvedPath), options)
            ?? throw new InvalidDataException($"Cue sheet is empty: {resolvedPath}");
        sheet.Validate(resolvedPath);
        return sheet;
    }

    public void Validate(string source)
    {
        if (string.IsNullOrWhiteSpace(Name) || DurationMs <= 0 || Sections.Count == 0)
        {
            throw new InvalidDataException($"Cue sheet '{source}' requires a name, durationMs, and sections.");
        }
        var previousEnd = 0;
        foreach (var section in Sections.OrderBy(section => section.StartMs))
        {
            if (section.StartMs != previousEnd || section.EndMs <= section.StartMs)
            {
                throw new InvalidDataException(
                    $"Cue sheet '{source}' has a gap, overlap, or invalid section at {section.StartMs}-{section.EndMs} ms.");
            }
            if (section.Pattern is not null && !Patterns.ContainsKey(section.Pattern))
            {
                throw new InvalidDataException(
                    $"Cue sheet '{source}' section '{section.Name}' references unknown pattern '{section.Pattern}'.");
            }
            var resolved = Resolve(section);
            if (resolved.Pan is null || resolved.Tilt is null || resolved.Dimmer is null || resolved.ShutterOpen is null)
            {
                throw new InvalidDataException(
                    $"Cue sheet '{source}' section '{section.Name}' does not resolve pan, tilt, dimmer, and shutterOpen.");
            }
            previousEnd = section.EndMs;
        }
        if (previousEnd != DurationMs)
        {
            throw new InvalidDataException(
                $"Cue sheet '{source}' ends at {previousEnd} ms; declared duration is {DurationMs} ms.");
        }
    }

    public CuePatternDefinition Resolve(CueSheetSection section)
    {
        var pattern = section.Pattern is null ? null : Patterns[section.Pattern];
        return Defaults.Overlay(pattern).Overlay(section.Overrides);
    }
}

internal static class CueSheetComposer
{
    private const int MinimumMovementDurationMs = 200;
    private const int MaximumTransitionDurationMs = 2400;
    private const int MinimumPreferredTransitionDurationMs = 1000;

    public static SequencePlan Apply(
        SequencePlan basePlan,
        CueSheet cueSheet,
        int headCount,
        FixtureProfileSettings fixtureProfile,
        IReadOnlyList<int>? beatTimes = null)
    {
        if (cueSheet.DurationMs != basePlan.Duration)
        {
            throw new InvalidDataException(
                $"Cue sheet duration {cueSheet.DurationMs} ms does not match sequence duration {basePlan.Duration} ms.");
        }

        var resolvedSections = cueSheet.Sections
            .Select(section => new ResolvedSection(section, cueSheet.Resolve(section)))
            .ToArray();
        var beatGrid = CreateBeatGrid(beatTimes, basePlan.Duration);
        var frameIntervalMs = Math.Max(1, basePlan.FrameIntervalMs);
        var panCues = CreateMovementCues(
            resolvedSections,
            headCount,
            fixtureProfile,
            MovementAxis.Pan,
            beatGrid,
            frameIntervalMs).ToArray();
        var tiltCues = CreateMovementCues(
            resolvedSections,
            headCount,
            fixtureProfile,
            MovementAxis.Tilt,
            beatGrid,
            frameIntervalMs).ToArray();
        var dimmerCues = CreateDimmerCues(
            resolvedSections,
            panCues,
            tiltCues,
            headCount,
            frameIntervalMs).ToArray();
        var shutterEvents = MergeShutterEvents(resolvedSections
            .Where(item => item.Pattern.ShutterOpen!.Value)
            .Select(item => new RootEffectEvent(item.Section.StartMs, item.Section.EndMs, "D100")));

        var definitions = EffectDefinitions.CreateBase();
        definitions.AddCueDefinitions(panCues.Concat(tiltCues).Concat(dimmerCues));
        if (shutterEvents.Length > 0 && !definitions.Contains("D100"))
        {
            definitions.AddOnHold("D100", 100);
        }
        return basePlan with
        {
            Definitions = definitions,
            PanCues = panCues,
            TiltCues = tiltCues,
            DimmerCues = dimmerCues,
            ShutterEvents = shutterEvents,
            PreserveLegacyDimmer = false,
        };
    }

    private static IEnumerable<Cue> CreateDimmerCues(
        IReadOnlyList<ResolvedSection> sections,
        IReadOnlyList<Cue> panCues,
        IReadOnlyList<Cue> tiltCues,
        int headCount,
        int frameIntervalMs)
    {
        var baseCues = CreateBaseDimmerCues(sections, tiltCues, headCount).ToArray();
        foreach (var item in sections)
        {
            var section = item.Section;
            var envelope = item.Pattern.IntensityEnvelope ?? IntensityEnvelope.Steady;
            var sectionCues = baseCues.Where(cue =>
                cue.Start < section.EndMs && cue.End > section.StartMs);
            if (envelope == IntensityEnvelope.Steady)
            {
                foreach (var cue in sectionCues)
                {
                    yield return cue;
                }
                continue;
            }

            var movementRanges = panCues.Concat(tiltCues)
                .Where(cue => cue.Start < section.EndMs && cue.End > section.StartMs &&
                    cue.Keys.Any(CueCurveSlicer.IsDynamicKey))
                .Select(cue => (cue.Start, cue.End))
                .Distinct()
                .OrderBy(range => range.Start)
                .ToArray();
            foreach (var cue in sectionCues)
            {
                var boundaries = new SortedSet<int> { cue.Start, cue.End };
                if (IsCurveEnvelope(envelope))
                {
                    IReadOnlyList<(int Start, int End)> envelopeRanges = movementRanges.Length > 0
                        ? movementRanges
                        : [(section.StartMs, section.EndMs)];
                    foreach (var range in envelopeRanges.Where(range =>
                                 range.Start < cue.End && range.End > cue.Start))
                    {
                        AddEnvelopeBoundaries(
                            boundaries,
                            range,
                            cue,
                            envelope,
                            frameIntervalMs);
                    }
                }
                var times = boundaries.ToArray();
                for (var boundaryIndex = 0; boundaryIndex < times.Length - 1; boundaryIndex++)
                {
                    var start = times[boundaryIndex];
                    var end = times[boundaryIndex + 1];
                    var keys = cue.Keys.Select(key => CreateEnvelopeKey(
                        key,
                        cue.Start,
                        cue.End,
                        start,
                        end,
                        section,
                        envelope,
                        movementRanges)).ToArray();
                    yield return Cue.Create(start, end, keys, headCount);
                }
            }
        }
    }

    private static IEnumerable<Cue> CreateBaseDimmerCues(
        IReadOnlyList<ResolvedSection> sections,
        IReadOnlyList<Cue> tiltCues,
        int headCount)
    {
        var allOff = PatternFactory.Dimmer(DimmerPattern.All0, headCount);
        foreach (var item in sections)
        {
            var section = item.Section;
            var baseKeys = ApplyFixtureSelection(
                ResolveKeys(
                    section.DimmerKeys,
                    PatternFactory.Dimmer(item.Pattern.Dimmer!.Value, headCount),
                    headCount,
                    "dimmer"),
                allOff,
                section.Heads,
                headCount);
            var rhythm = item.Pattern.Rhythm ?? RhythmPattern.Off;
            if (!IsFixtureChase(rhythm))
            {
                yield return Cue.Create(section.StartMs, section.EndMs, baseKeys, headCount);
                continue;
            }

            var cursor = section.StartMs;
            foreach (var tiltCue in tiltCues.Where(cue =>
                         cue.Start < section.EndMs && cue.End > section.StartMs))
            {
                var start = Math.Max(section.StartMs, tiltCue.Start);
                var end = Math.Min(section.EndMs, tiltCue.End);
                if (start > cursor)
                {
                    yield return Cue.Create(
                        cursor,
                        start,
                        SliceDimmerKeys(baseKeys, section, cursor, start),
                        headCount);
                }

                var activeHeads = tiltCue.Keys
                    .Select(CueCurveSlicer.IsDynamicKey)
                    .ToArray();
                var sliced = SliceDimmerKeys(baseKeys, section, start, end);
                var keys = activeHeads.Any(active => active)
                    ? sliced.Select((key, index) => activeHeads[index] ? key : "D0").ToArray()
                    : sliced;
                yield return Cue.Create(start, end, keys, headCount);
                cursor = end;
            }
            if (cursor < section.EndMs)
            {
                yield return Cue.Create(
                    cursor,
                    section.EndMs,
                    SliceDimmerKeys(baseKeys, section, cursor, section.EndMs),
                    headCount);
            }
        }
    }

    private static void AddEnvelopeBoundaries(
        ISet<int> boundaries,
        (int Start, int End) movement,
        Cue cue,
        IntensityEnvelope envelope,
        int frameIntervalMs)
    {
        var duration = movement.End - movement.Start;
        foreach (var point in EnvelopePoints(envelope))
        {
            var rawTime = movement.Start + (int)Math.Round(
                duration * point.Progress,
                MidpointRounding.AwayFromZero);
            var time = AlignTime(rawTime, frameIntervalMs);
            if (time > cue.Start && time < cue.End)
            {
                boundaries.Add(time);
            }
        }
    }

    private static string CreateEnvelopeKey(
        string key,
        int cueStart,
        int cueEnd,
        int start,
        int end,
        CueSheetSection section,
        IntensityEnvelope envelope,
        IReadOnlyList<(int Start, int End)> movementRanges)
    {
        var startValue = EvaluateDimmerKey(key, cueStart, cueEnd, start) *
            EvaluateEnvelope(envelope, section, movementRanges, start);
        var endValue = EvaluateDimmerKey(key, cueStart, cueEnd, end) *
            EvaluateEnvelope(envelope, section, movementRanges, end);
        var first = Math.Clamp(
            (int)Math.Round(startValue, MidpointRounding.AwayFromZero),
            0,
            100);
        var second = Math.Clamp(
            (int)Math.Round(endValue, MidpointRounding.AwayFromZero),
            0,
            100);
        return first == second ? $"D{first}" : $"D{first}_{second}";
    }

    private static double EvaluateDimmerKey(
        string key,
        int start,
        int end,
        int time)
    {
        var values = key.Length > 1
            ? key[1..].Split('_').Select(int.Parse).ToArray()
            : [0];
        if (values.Length < 2 || end <= start)
        {
            return values[0];
        }
        var progress = Math.Clamp((double)(time - start) / (end - start), 0, 1);
        return values[0] + ((values[1] - values[0]) * progress);
    }

    private static double EvaluateEnvelope(
        IntensityEnvelope envelope,
        CueSheetSection section,
        IReadOnlyList<(int Start, int End)> movementRanges,
        int time)
    {
        var sectionProgress = Math.Clamp(
            (double)(time - section.StartMs) / (section.EndMs - section.StartMs),
            0,
            1);
        if (envelope == IntensityEnvelope.FadeIn)
        {
            return sectionProgress;
        }
        if (envelope == IntensityEnvelope.FadeOut)
        {
            return 1 - sectionProgress;
        }

        var movement = movementRanges.FirstOrDefault(range =>
            time >= range.Start && time <= range.End);
        var progress = movement.End > movement.Start
            ? Math.Clamp((double)(time - movement.Start) / (movement.End - movement.Start), 0, 1)
            : sectionProgress;
        var points = EnvelopePoints(envelope);
        for (var index = 1; index < points.Length; index++)
        {
            if (progress > points[index].Progress)
            {
                continue;
            }
            var left = points[index - 1];
            var right = points[index];
            var span = right.Progress - left.Progress;
            return span <= 0
                ? right.Level
                : left.Level + ((right.Level - left.Level) *
                    ((progress - left.Progress) / span));
        }
        return points.Length == 0 ? 1 : points[^1].Level;
    }

    private static bool IsCurveEnvelope(IntensityEnvelope envelope) => envelope is
        IntensityEnvelope.Pulse or IntensityEnvelope.Turnaround or
        IntensityEnvelope.Flash or IntensityEnvelope.RiseAndHold or
        IntensityEnvelope.TexturedPulse;

    private static (double Progress, double Level)[] EnvelopePoints(
        IntensityEnvelope envelope) => envelope switch
    {
        IntensityEnvelope.Pulse => [(0, 0), (0.5, 1), (1, 0)],
        IntensityEnvelope.Turnaround =>
            [(0, 0.2), (0.38, 0.2), (0.5, 1), (0.62, 0.2), (1, 0.2)],
        IntensityEnvelope.Flash => [(0, 0), (1d / 3, 0), (2d / 3, 1), (1, 0)],
        IntensityEnvelope.RiseAndHold => [(0, 0), (1d / 3, 1), (2d / 3, 1), (1, 1)],
        IntensityEnvelope.TexturedPulse =>
        [
            (0, 0),
            (1d / 7, 0),
            (2d / 7, 0.075),
            (3d / 7, 1),
            (4d / 7, 0.60625),
            (5d / 7, 1),
            (6d / 7, 1),
            (1, 0),
        ],
        _ => [],
    };

    private static string[] SliceDimmerKeys(
        IReadOnlyList<string> keys,
        CueSheetSection section,
        int start,
        int end) => keys
            .Select(key => CueCurveSlicer.SliceKey(
                string.IsNullOrWhiteSpace(key) ? "D0" : key,
                section.StartMs,
                section.EndMs,
                start,
                end))
            .ToArray();

    private static bool IsFixtureChase(RhythmPattern rhythm) => rhythm is
        RhythmPattern.TiltHeadChaseEveryBeat or RhythmPattern.TiltPairChaseEveryBeat;

    private static IEnumerable<Cue> CreateMovementCues(
        IReadOnlyList<ResolvedSection> sections,
        int headCount,
        FixtureProfileSettings fixtureProfile,
        MovementAxis axis,
        IReadOnlyList<int> beatGrid,
        int frameIntervalMs)
    {
        for (var index = 0; index < sections.Count; index++)
        {
            var item = sections[index];
            var rhythm = item.Pattern.Rhythm ?? RhythmPattern.Off;
            var inactiveKeys = axis == MovementAxis.Pan
                ? PatternFactory.Pan(PanPattern.Park, headCount, fixtureProfile)
                : PatternFactory.Tilt(TiltPattern.Park, headCount, fixtureProfile);
            if (!UsesAxis(rhythm, axis))
            {
                var sectionEnergy = item.Pattern.MotionEnergy ?? MotionEnergy.Full;
                var patternKeys = axis == MovementAxis.Pan
                    ? PatternFactory.Pan(item.Pattern.Pan!.Value, headCount, fixtureProfile, sectionEnergy)
                    : PatternFactory.Tilt(item.Pattern.Tilt!.Value, headCount, fixtureProfile, sectionEnergy);
                var explicitKeys = axis == MovementAxis.Pan
                    ? item.Section.PanKeys
                    : item.Section.TiltKeys;
                var normalKeys = ResolveKeys(
                    explicitKeys,
                    patternKeys,
                    headCount,
                    axis == MovementAxis.Pan ? "pan" : "tilt");
                if (explicitKeys is null)
                {
                    var sectionShape = item.Pattern.MotionShape ?? MotionShape.Smooth;
                    var sectionPhase = item.Pattern.MotionPhase ?? MotionPhase.Together;
                    normalKeys = normalKeys.Select((key, fixtureIndex) =>
                        MotionCurveKey.ApplyStyle(
                            key,
                            sectionShape,
                            sectionPhase,
                            fixtureIndex,
                            headCount)).ToArray();
                }
                normalKeys = normalKeys.Select((key, fixtureIndex) => ClampMovementKey(
                    key,
                    axis == MovementAxis.Pan
                        ? fixtureProfile.PanFor(fixtureIndex)
                        : fixtureProfile.TiltFor(fixtureIndex))).ToArray();
                var selectedKeys = ApplyFixtureSelection(
                    normalKeys,
                    inactiveKeys,
                    item.Section.Heads,
                    headCount);
                var transitionEnd = FindTransitionEnd(item.Section, selectedKeys, beatGrid);
                yield return Cue.Create(
                    item.Section.StartMs,
                    transitionEnd,
                    selectedKeys,
                    headCount);
                if (transitionEnd < item.Section.EndMs)
                {
                    yield return Cue.Create(
                        transitionEnd,
                        item.Section.EndMs,
                        selectedKeys.Select(CueCurveSlicer.EndKey).ToArray(),
                        headCount);
                }
                continue;
            }
            if (beatGrid.Count < 2)
            {
                throw new InvalidDataException(
                    $"Cue-sheet section '{item.Section.Name}' uses {rhythm}, but the sequence has no usable beat timing.");
            }

            var panAnchor = StablePanAnchor(item.Pattern.Pan!.Value);
            var tiltAnchor = StableTiltAnchor(item.Pattern.Tilt!.Value);
            var energy = item.Pattern.MotionEnergy ?? MotionEnergy.Full;
            var shape = item.Pattern.MotionShape ?? MotionShape.Smooth;
            var phase = item.Pattern.MotionPhase ?? MotionPhase.Together;
            var runEndIndex = index;
            while (runEndIndex + 1 < sections.Count)
            {
                var next = sections[runEndIndex + 1];
                if ((next.Pattern.Rhythm ?? RhythmPattern.Off) != rhythm ||
                    !next.Section.Heads.SequenceEqual(item.Section.Heads) ||
                    (next.Pattern.MotionEnergy ?? MotionEnergy.Full) != energy ||
                    (next.Pattern.MotionShape ?? MotionShape.Smooth) != shape ||
                    (next.Pattern.MotionPhase ?? MotionPhase.Together) != phase ||
                    (axis == MovementAxis.Pan &&
                        StablePanAnchor(next.Pattern.Pan!.Value) != panAnchor))
                {
                    break;
                }
                runEndIndex++;
            }

            var holdKeys = axis == MovementAxis.Pan
                ? PatternFactory.Pan(panAnchor, headCount, fixtureProfile)
                : PatternFactory.Tilt(tiltAnchor, headCount, fixtureProfile);
            IReadOnlyList<string> RhythmKeysForCycle(int cycleIndex)
            {
                var rhythmKeys = (rhythm, axis) switch
                {
                    (RhythmPattern.TiltOddEvenEveryBeat, MovementAxis.Tilt) =>
                        PatternFactory.TiltOddEvenBounce(
                            headCount,
                            oddActive: cycleIndex % 2 == 0,
                            fixtureProfile,
                            energy),
                    (RhythmPattern.TiltCenterOuterEveryBeat, MovementAxis.Tilt) =>
                        PatternFactory.TiltCenterOuterBounce(
                            headCount,
                            outerActive: cycleIndex % 2 == 0,
                            fixtureProfile,
                            energy),
                    (RhythmPattern.TiltHeadChaseEveryBeat, MovementAxis.Tilt) =>
                        PatternFactory.TiltHeadChase(
                            headCount,
                            cycleIndex,
                            fixtureProfile,
                            energy),
                    (RhythmPattern.TiltPairChaseEveryBeat, MovementAxis.Tilt) =>
                        PatternFactory.TiltPairChase(
                            headCount,
                            cycleIndex,
                            fixtureProfile,
                            energy),
                    (RhythmPattern.TiltLiftEveryBeat, MovementAxis.Tilt) =>
                        PatternFactory.Tilt(TiltPattern.Bounce, headCount, fixtureProfile, energy),
                    _ => axis == MovementAxis.Pan
                            ? PatternFactory.PanRhythmBounce(
                                panAnchor,
                                headCount,
                                fixtureProfile,
                                energy)
                            : PatternFactory.Tilt(
                                TiltPattern.InverseBounce,
                                headCount,
                                fixtureProfile,
                                energy),
                };
                var selected = ApplyFixtureSelection(
                    rhythmKeys,
                    inactiveKeys,
                    item.Section.Heads,
                    headCount);
                return selected.Select((key, fixtureIndex) => MotionCurveKey.ApplyStyle(
                    key,
                    shape,
                    phase,
                    fixtureIndex,
                    headCount)).ToArray();
            }
            foreach (var cue in CreateRhythmRunCues(
                item.Section.StartMs,
                sections[runEndIndex].Section.EndMs,
                rhythm,
                RhythmKeysForCycle,
                ApplyFixtureSelection(holdKeys, inactiveKeys, item.Section.Heads, headCount),
                headCount,
                beatGrid,
                frameIntervalMs))
            {
                yield return cue;
            }
            index = runEndIndex;
        }
    }

    private static string ClampMovementKey(string key, AxisProfileSettings axis) =>
        Regex.Replace(
            key,
            "\\d+",
            match => Math.Clamp(int.Parse(match.Value), axis.Minimum, axis.Maximum).ToString(),
            RegexOptions.CultureInvariant);

    private static int FindTransitionEnd(
        CueSheetSection section,
        IReadOnlyList<string> keys,
        IReadOnlyList<int> beatGrid)
    {
        if (section.EndMs - section.StartMs <= MaximumTransitionDurationMs ||
            !keys.Any(CueCurveSlicer.IsDynamicKey))
        {
            return section.EndMs;
        }

        var maximumEnd = Math.Min(
            section.EndMs,
            section.StartMs + MaximumTransitionDurationMs);
        return beatGrid
            .Where(time => time >= section.StartMs + MinimumPreferredTransitionDurationMs &&
                time <= maximumEnd)
            .DefaultIfEmpty(maximumEnd)
            .Max();
    }

    private static IEnumerable<Cue> CreateRhythmRunCues(
        int runStart,
        int runEnd,
        RhythmPattern rhythm,
        Func<int, IReadOnlyList<string>> rhythmKeysForCycle,
        IReadOnlyList<string> holdKeys,
        int headCount,
        IReadOnlyList<int> beatGrid,
        int frameIntervalMs)
    {
        if (rhythm is RhythmPattern.TiltPulseEveryHalfBeat or RhythmPattern.TiltSyncopatedPulse)
        {
            foreach (var cue in CreateSubdividedRhythmRunCues(
                runStart,
                runEnd,
                rhythm,
                rhythmKeysForCycle,
                holdKeys,
                headCount,
                beatGrid,
                frameIntervalMs))
            {
                yield return cue;
            }
            yield break;
        }
        var beatsPerCycle = BeatsPerCycle(rhythm);
        var nearestBeatIndex = Enumerable.Range(0, beatGrid.Count)
            .MinBy(index => Math.Abs(beatGrid[index] - runStart));
        var phaseOffset = runStart - beatGrid[nearestBeatIndex];
        var cycleStart = runStart;
        var cycleIndex = nearestBeatIndex;
        for (var index = nearestBeatIndex + beatsPerCycle; index < beatGrid.Count; index += beatsPerCycle)
        {
            var cycleEnd = beatGrid[index] + phaseOffset;
            if (cycleEnd <= cycleStart)
            {
                continue;
            }
            if (cycleEnd > runEnd)
            {
                var tailKeys = IsFixtureChase(rhythm) &&
                    runEnd - cycleStart >= MinimumMovementDurationMs
                        ? rhythmKeysForCycle(cycleIndex)
                        : holdKeys;
                yield return Cue.Create(cycleStart, runEnd, tailKeys, headCount);
                yield break;
            }
            yield return Cue.Create(cycleStart, cycleEnd, rhythmKeysForCycle(cycleIndex), headCount);
            cycleStart = cycleEnd;
            cycleIndex++;
            if (cycleStart >= runEnd)
            {
                yield break;
            }
        }
        if (cycleStart < runEnd)
        {
            yield return Cue.Create(cycleStart, runEnd, holdKeys, headCount);
        }
    }

    private static IEnumerable<Cue> CreateSubdividedRhythmRunCues(
        int runStart,
        int runEnd,
        RhythmPattern rhythm,
        Func<int, IReadOnlyList<string>> rhythmKeysForCycle,
        IReadOnlyList<string> holdKeys,
        int headCount,
        IReadOnlyList<int> beatGrid,
        int frameIntervalMs)
    {
        var grid = beatGrid
            .Zip(beatGrid.Skip(1), (left, right) => new[]
            {
                left,
                AlignTime(left + ((right - left) / 2), frameIntervalMs),
            })
            .SelectMany(times => times)
            .Append(beatGrid[^1])
            .Distinct()
            .Order()
            .ToArray();
        var nearestIndex = Enumerable.Range(0, grid.Length)
            .MinBy(index => Math.Abs(grid[index] - runStart));
        var phaseOffset = runStart - grid[nearestIndex];
        var cycleStart = runStart;
        var cycleIndex = nearestIndex;
        var stepIndex = 0;
        var syncopatedSteps = new[] { 1, 2, 1 };
        var gridIndex = nearestIndex;
        while (gridIndex < grid.Length - 1)
        {
            var step = rhythm == RhythmPattern.TiltSyncopatedPulse
                ? syncopatedSteps[stepIndex++ % syncopatedSteps.Length]
                : 1;
            gridIndex += step;
            if (gridIndex >= grid.Length)
            {
                break;
            }
            var cycleEnd = grid[gridIndex] + phaseOffset;
            if (cycleEnd <= cycleStart)
            {
                continue;
            }
            if (cycleEnd > runEnd)
            {
                yield return Cue.Create(cycleStart, runEnd, holdKeys, headCount);
                yield break;
            }
            yield return Cue.Create(cycleStart, cycleEnd, rhythmKeysForCycle(cycleIndex++), headCount);
            cycleStart = cycleEnd;
            if (cycleStart >= runEnd)
            {
                yield break;
            }
        }
        if (cycleStart < runEnd)
        {
            yield return Cue.Create(cycleStart, runEnd, holdKeys, headCount);
        }
    }

    private static int AlignTime(int time, int frameIntervalMs) =>
        (int)Math.Round(
            (double)time / frameIntervalMs,
            MidpointRounding.AwayFromZero) * frameIntervalMs;

    private static int[] CreateBeatGrid(IReadOnlyList<int>? beatTimes, int duration)
    {
        var beats = beatTimes?
            .Where(time => time >= 0 && time <= duration)
            .Distinct()
            .Order()
            .ToList() ?? [];
        if (beats.Count < 2)
        {
            return [];
        }
        var gaps = beats.Zip(beats.Skip(1), (left, right) => right - left)
            .Where(gap => gap > 0)
            .Order()
            .ToArray();
        var step = gaps[gaps.Length / 2];
        while (beats[0] > 0)
        {
            beats.Insert(0, beats[0] - step);
        }
        while (beats[^1] < duration + (step * 4))
        {
            beats.Add(beats[^1] + step);
        }
        return [.. beats];
    }

    private static bool UsesAxis(RhythmPattern rhythm, MovementAxis axis) => rhythm switch
    {
        RhythmPattern.TiltLiftEveryBeat or RhythmPattern.TiltPulseEveryHalfBeat or
            RhythmPattern.TiltBounceEveryBeat or RhythmPattern.TiltSyncopatedPulse or
            RhythmPattern.TiltBounceEveryTwoBeats or
            RhythmPattern.TiltOddEvenEveryBeat or RhythmPattern.TiltCenterOuterEveryBeat or
            RhythmPattern.TiltHeadChaseEveryBeat or RhythmPattern.TiltPairChaseEveryBeat =>
            axis == MovementAxis.Tilt,
        RhythmPattern.PanBounceEveryTwoBeats => axis == MovementAxis.Pan,
        RhythmPattern.PanTiltBounceEveryFourBeats => true,
        _ => false,
    };

    private static int BeatsPerCycle(RhythmPattern rhythm) => rhythm switch
    {
        RhythmPattern.TiltLiftEveryBeat or RhythmPattern.TiltBounceEveryBeat or
            RhythmPattern.TiltOddEvenEveryBeat or
            RhythmPattern.TiltCenterOuterEveryBeat or
            RhythmPattern.TiltHeadChaseEveryBeat or RhythmPattern.TiltPairChaseEveryBeat => 1,
        RhythmPattern.TiltBounceEveryTwoBeats or RhythmPattern.PanBounceEveryTwoBeats => 2,
        RhythmPattern.PanTiltBounceEveryFourBeats => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(rhythm)),
    };

    private static PanPattern StablePanAnchor(PanPattern pattern) => pattern switch
    {
        PanPattern.OppositeFan or PanPattern.Cross => PanPattern.OppositeFan,
        _ => PanPattern.Fan,
    };

    private static TiltPattern StableTiltAnchor(TiltPattern pattern) => pattern switch
    {
        TiltPattern.Park or TiltPattern.Fall or TiltPattern.Bounce => TiltPattern.Park,
        _ => TiltPattern.High,
    };

    private enum MovementAxis
    {
        Pan,
        Tilt,
    }

    private sealed record ResolvedSection(
        CueSheetSection Section,
        CuePatternDefinition Pattern);

    private static RootEffectEvent[] MergeShutterEvents(IEnumerable<RootEffectEvent> events)
    {
        var merged = new List<RootEffectEvent>();
        foreach (var effect in events.OrderBy(effect => effect.Start))
        {
            if (merged.Count > 0 && merged[^1].End == effect.Start)
            {
                merged[^1] = merged[^1] with { End = effect.End };
            }
            else
            {
                merged.Add(effect);
            }
        }
        return [.. merged];
    }

    private static string[] ApplyFixtureSelection(
        IReadOnlyList<string> selectedValues,
        IReadOnlyList<string> inactiveValues,
        IReadOnlyList<int> heads,
        int headCount)
    {
        if (heads.Count == 0)
        {
            return [.. selectedValues];
        }
        if (heads.Any(head => head < 1 || head > headCount) || heads.Distinct().Count() != heads.Count)
        {
            throw new InvalidDataException(
                $"Cue-sheet fixture selection must contain unique head numbers from 1 through {headCount}.");
        }
        var selected = heads.ToHashSet();
        return Enumerable.Range(0, headCount)
            .Select(index => selected.Contains(index + 1) ? selectedValues[index] : inactiveValues[index])
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveKeys(
        IReadOnlyList<string>? explicitKeys,
        IReadOnlyList<string> patternKeys,
        int headCount,
        string role)
    {
        if (explicitKeys is null)
        {
            return patternKeys;
        }
        if (explicitKeys.Count != headCount)
        {
            throw new InvalidDataException(
                $"Cue-sheet {role} override has {explicitKeys.Count} fixture keys; expected {headCount}.");
        }
        return explicitKeys;
    }
}