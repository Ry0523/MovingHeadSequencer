using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using MovingHeadSequencer.Audit;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Cli;
using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Application;
using MovingHeadSequencer.Domain;
using MovingHeadSequencer.Generation;
using MovingHeadSequencer.Layout;
using MovingHeadSequencer.Library;
using MovingHeadSequencer.Preview;
using MovingHeadSequencer.Sequences;
using MovingHeadSequencer.Suggestions;
using MovingHeadSequencer.Timing;
using MovingHeadSequencer.Validation;
using MovingHeadSequencer.Web;

namespace MovingHeadSequencer.Tests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            VerifyPatterns();
            VerifyConfiguration();
            VerifyCueSheets();
            VerifyPreviewCompiler();
            VerifyTimingMaps();
            VerifyWebEditor();
            VerifyBatchManifest();
            VerifyLibraryIntelligence();
            VerifyRestraintPolicies();
            VerifyLegacyStormShutterEmission();
            VerifyPostWriteValidation();
            VerifyValidation();
            VerifyArchiveSelection();
            VerifyLayoutDiscovery();
            VerifySequenceSelection();
            Console.WriteLine("All component checks passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void VerifyPatterns()
    {
        foreach (var headCount in Enumerable.Range(2, 11))
        {
            Equal(headCount, PatternFactory.EvenPanValues(headCount).Length,
                $"{headCount}-head pan count");
            Equal(headCount, PatternFactory.Tilt(TiltPattern.High, headCount).Length,
                $"{headCount}-head tilt count");
            Equal(headCount, PatternFactory.Dimmer(DimmerPattern.OuterHigh, headCount).Length,
                $"{headCount}-head dimmer count");
            var symmetric = PatternFactory.SymmetricValues(headCount, 100, 0);
            EqualSequence(symmetric, symmetric.Reverse(), $"{headCount}-head symmetry");
        }
        EqualSequence([130, 210], PatternFactory.EvenPanValues(2), "two-head pan");
        EqualSequence(["T85", "T100", "T85"], PatternFactory.Tilt(TiltPattern.High, 3),
            "three-head center tilt");
        EqualSequence(["D100", "D50", "D0", "D50", "D100"],
            PatternFactory.Dimmer(DimmerPattern.OuterOnly, 5), "five-head center dimmer");
        EqualSequence([130, 150, 190, 210], PatternFactory.EvenPanValues(4), "four-head pan");
        EqualSequence([130, 146, 162, 178, 194, 210], PatternFactory.EvenPanValues(6), "six-head pan");
        EqualSequence([130, 141, 153, 164, 176, 187, 199, 210], PatternFactory.EvenPanValues(8), "eight-head pan");
        EqualSequence(
            ["T85", "T90", "T95", "T100", "T100", "T95", "T90", "T85"],
            PatternFactory.Tilt(TiltPattern.High, 8),
            "eight-head tilt");
        EqualSequence(
            ["D100", "D90", "D80", "D70", "D70", "D80", "D90", "D100"],
            PatternFactory.Dimmer(DimmerPattern.OuterHigh, 8),
            "eight-head dimmer");

        var profile = new FixtureProfileSettings
        {
            Pan = new AxisProfileSettings(10, 240, 120, 40, 200, true),
            Tilt = new AxisProfileSettings(20, 180, 60, 80, 130, true),
        };
        EqualSequence([210, 170, 90, 50], PatternFactory.EvenPanValues(4, profile), "profile pan inversion");
        EqualSequence(
            ["T120", "T70", "T70", "T120"],
            PatternFactory.Tilt(TiltPattern.High, 4, profile),
            "profile tilt inversion");

        var asymmetric = new FixtureProfileSettings
        {
            Fixtures = new Dictionary<string, FixtureAxisOverrideSettings>
            {
                ["Mover1"] = new()
                {
                    Pan = new AxisProfileSettings(0, 255, 100, 20, 220, true),
                },
                ["Mover4"] = new()
                {
                    Tilt = new AxisProfileSettings(0, 200, 40, 60, 160, true),
                },
            },
        }.ResolveFixtures(["Mover1", "Mover2", "Mover3", "Mover4"]);
        EqualSequence([235, 150, 190, 210], PatternFactory.EvenPanValues(4, asymmetric), "per-fixture pan override");
        EqualSequence(
            ["T85", "T100", "T100", "T140"],
            PatternFactory.Tilt(TiltPattern.High, 4, asymmetric),
            "per-fixture tilt override");
    }

        private static void VerifyConfiguration()
        {
                var defaults = GenerationSettings.Load(null);
                Equal(4, defaults.HeadCount, "default configured head count");
                Equal(170, defaults.FixtureProfile.Pan.Park, "default pan park");
                Equal(540, defaults.FixtureProfile.PanTravelDegrees, "default pan travel degrees");
                Equal(270, defaults.FixtureProfile.TiltTravelDegrees, "default tilt travel degrees");

                using var temporary = new TemporaryDirectory();
                var path = Path.Combine(temporary.Path, "settings.json");
                File.WriteAllText(path, """
                        {
                            "headCount": 6,
                            "fixtureProfile": {
                                "name": "Test",
                                "panTravelDegrees": 630,
                                "tiltTravelDegrees": 230,
                                "pan": { "minimum": 10, "maximum": 240, "park": 120, "outer": 40, "center": 200, "inverted": true },
                                "tilt": { "minimum": 20, "maximum": 180, "park": 60, "outer": 80, "center": 130, "inverted": false }
                            },
                            "validation": { "minimumMovementDurationMs": 300, "maximumPanDelta": 150, "maximumTiltDelta": 75, "maximumDynamicMotionPercent": 60 },
                            "audit": { "minimumGapMs": 1500, "nearDarkThresholdPercent": 5 }
                        }
                        """);
                var configured = GenerationSettings.Load(path);
                Equal(6, configured.HeadCount, "JSON head count");
                Equal(120, configured.FixtureProfile.Pan.Park, "JSON pan park");
                True(configured.FixtureProfile.Pan.Inverted, "JSON pan inversion");
                Equal(630, configured.FixtureProfile.PanTravelDegrees, "JSON pan travel degrees");
                Equal(230, configured.FixtureProfile.TiltTravelDegrees, "JSON tilt travel degrees");
                Throws<InvalidDataException>(
                    () => (new FixtureProfileSettings { PanTravelDegrees = 0 }).Validate("invalid travel"),
                    "invalid fixture travel degrees");

                var resolved = configured.Resolve(8, true, "layout.xml", "Roof", null, null, null, "song.xsq");
                Equal(8, resolved.HeadCount, "CLI head count wins");
                True(resolved.AllowLayoutWarnings, "CLI warning override wins");
                Equal("layout.xml", resolved.RgbEffectsPath, "CLI layout wins");

                var options = CommandLineOptions.Parse(["--target", "All", "--config", path, "--head-count", "8"]);
                True(options.HeadCountSpecified, "CLI head-count override marker");
                Equal(path, options.ConfigPath, "CLI configuration path");
                Equal(2, CommandLineOptions.Parse(["--target", "All", "--head-count", "2"]).HeadCount,
                    "CLI minimum head count");
                Equal(12, CommandLineOptions.Parse(["--target", "All", "--head-count", "12"]).HeadCount,
                    "CLI maximum head count");
                Throws<CommandLineException>(
                    () => CommandLineOptions.Parse(["--target", "All", "--head-count", "1"]),
                    "CLI rejects head count below range");
                Throws<CommandLineException>(
                    () => CommandLineOptions.Parse(["--target", "All", "--head-count", "13"]),
                    "CLI rejects head count above range");
        }

    private static void VerifyRestraintPolicies()
    {
        var storm = StormSequenceDefinition.Create(4, layoutDriven: true);
        Equal(71500, DynamicCoverage(storm.PanCues), "Storm dynamic pan coverage");
        Equal(71500, DynamicCoverage(storm.TiltCues), "Storm dynamic tilt coverage");
        EqualSequence(
            [
                "11625-14100", "18450-20975", "27400-52275", "58675-65125",
                "71125-92550", "106275-133700", "141425-158550", "171400-211825",
            ],
            storm.ShutterEvents.Select(FormatInterval),
            "Storm shutter gates");

        var lilJon = LilJonSequenceDefinition.Create(4);
        Equal(51125, DynamicCoverage(lilJon.PanCues), "Lil Jon dynamic pan coverage");
        Equal(51125, DynamicCoverage(lilJon.TiltCues), "Lil Jon dynamic tilt coverage");
        EqualSequence(
            [
                "9050-19275", "19525-28425", "28550-47950", "52100-71625",
                "72025-79225", "88575-105725", "115300-124750", "133950-148550",
                "153650-166000",
            ],
            lilJon.ShutterEvents.Select(FormatInterval),
            "Lil Jon shutter gates");
    }

    private static void VerifyLegacyStormShutterEmission()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = FindRepositoryFile("A-Christmas-Storm(old_layout).xsq");
        var outputPath = Path.Combine(temporary.Path, "storm-legacy.xsq");
        var plan = StormSequenceDefinition.Create(4, layoutDriven: false) with
        {
            SourceFileName = sourcePath,
            OutputFileName = outputPath,
        };

        new SequenceGenerator(temporary.Path, force: true, TextWriter.Null).Generate(plan, null, 4);

        var document = new XmlDocument();
        document.Load(outputPath);
        var source = new XmlDocument();
        source.Load(sourcePath);
        foreach (var fixtureName in new[] { "Mover1", "Mover2", "Mover3", "Mover4" })
        {
            Equal(
                source.SelectSingleNode($"/xsequence/ElementEffects/Element[@type='model' and @name='{fixtureName}']")?.OuterXml,
                document.SelectSingleNode($"/xsequence/ElementEffects/Element[@type='model' and @name='{fixtureName}']")?.OuterXml,
                $"legacy Storm preserves {fixtureName}");
        }
        var intervals = document
            .SelectNodes("/xsequence/ElementEffects/Element[@type='model' and @name='MH-Shutters']/EffectLayer/Effect")!
            .Cast<XmlElement>()
            .Select(effect => $"{effect.GetAttribute("startTime")}-{effect.GetAttribute("endTime")}");
        EqualSequence(
            [
                "11625-14100", "18450-20975", "27400-52275", "58675-65125",
                "71125-92550", "106275-133700", "141425-158550", "171400-211825",
            ],
            intervals,
            "legacy Storm emitted shutter gates");
    }

    private static void VerifyPostWriteValidation()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "duration-mismatch.xsq");
        var source = new XmlDocument();
        source.Load(FindRepositoryFile("A-Christmas-Storm(old_layout).xsq"));
        source.SelectSingleNode("/xsequence/head/sequenceDuration")!.InnerText = "not-a-duration";
        source.Save(sourcePath);

        var outputPath = Path.Combine(temporary.Path, "existing-output.xsq");
        File.WriteAllText(outputPath, "existing output must survive");
        var plan = StormSequenceDefinition.Create(4, layoutDriven: false) with
        {
            SourceFileName = sourcePath,
            OutputFileName = outputPath,
        };

        Throws<InvalidDataException>(
            () => new SequenceGenerator(temporary.Path, force: true, TextWriter.Null)
                .Generate(plan, null, 4),
            "post-write sequence duration validation");
        Equal("existing output must survive", File.ReadAllText(outputPath),
            "post-write failure preserves existing output");

        var validOutputPath = Path.Combine(temporary.Path, "valid-output.xsq");
        var validPlan = StormSequenceDefinition.Create(4, layoutDriven: false) with
        {
            SourceFileName = FindRepositoryFile("A-Christmas-Storm(old_layout).xsq"),
            OutputFileName = validOutputPath,
        };
        new SequenceGenerator(temporary.Path, force: true, TextWriter.Null)
            .Generate(validPlan, null, 4);
        var corrupted = new XmlDocument();
        corrupted.Load(validOutputPath);
        var effectCount = corrupted.SelectNodes("/xsequence/EffectDB/Effect")!.Count;
        var generatedEffect = (XmlElement)corrupted.SelectSingleNode(
            "/xsequence/ElementEffects/Element[@name='MH-Pan']/Strand/Node/Effect")!;
        generatedEffect.SetAttribute("ref", effectCount.ToString());
        corrupted.Save(validOutputPath);
        Throws<InvalidDataException>(
            () => GeneratedXsqValidator.Validate(validOutputPath, validPlan, null, 4),
            "post-write effect reference validation");
    }

        private static void VerifyCueSheets()
        {
                using var temporary = new TemporaryDirectory();
                var path = Path.Combine(temporary.Path, "cue-sheet.json");
                File.WriteAllText(path, """
                        {
                            "name": "Test choreography",
                            "durationMs": 166000,
                            "defaults": { "pan": "Park", "tilt": "Park", "dimmer": "All0", "shutterOpen": false },
                            "patterns": {
                                "Reveal": { "pan": "OpenFan", "tilt": "Rise", "dimmer": "All75", "shutterOpen": true }
                            },
                            "sections": [
                                { "name": "Intro", "startMs": 0, "endMs": 9050 },
                                { "name": "Reveal", "startMs": 9050, "endMs": 166000, "pattern": "Reveal" }
                            ]
                        }
                        """);
                var sheet = CueSheet.Load(path);
                var plan = CueSheetComposer.Apply(
                        LilJonSequenceDefinition.Create(4),
                        sheet,
                        4,
                        new FixtureProfileSettings());
                Equal(3, plan.PanCues.Count, "cue-sheet pan sections");
                EqualSequence(["P170", "P170", "P170", "P170"], plan.PanCues[0].Keys, "cue-sheet defaults");
                EqualSequence(["P170_130", "P170_150", "P170_190", "P170_210"],
                    plan.PanCues[1].Keys, "cue-sheet named pattern");
                EqualSequence(["P130", "P150", "P190", "P210"],
                    plan.PanCues[2].Keys, "cue-sheet named pattern destination hold");
                EqualSequence(["0-9050", "9050-11450", "11450-166000"],
                    plan.PanCues.Select(cue => $"{cue.Start}-{cue.End}"),
                    "cue-sheet bounded transition timing");
                EqualSequence(["9050-166000"], plan.ShutterEvents.Select(FormatInterval), "cue-sheet shutter merge");

                var invalidPath = Path.Combine(temporary.Path, "invalid-cue-sheet.json");
                File.WriteAllText(invalidPath, """
                        {
                            "name": "Invalid",
                            "durationMs": 1000,
                            "defaults": { "pan": "Park", "tilt": "Park", "dimmer": "All0", "shutterOpen": false },
                            "sections": [{ "name": "Bad", "startMs": 0, "endMs": 1000, "pattern": "Missing" }]
                        }
                        """);
                Throws<InvalidDataException>(() => CueSheet.Load(invalidPath), "unknown cue-sheet pattern");
        }

            private static void VerifyPreviewCompiler()
            {
                var plan = LilJonSequenceDefinition.Create(4);
                var preview = PreviewCompiler.Compile(plan, 4, new FixtureProfileSettings());
                Equal(4, preview.Fixtures.Count, "preview fixture count");
                var openingRamp = preview.Fixtures[0].Pan.Single(segment => segment.StartMs == 9050);
                Equal(PreviewCurveKind.Linear, openingRamp.Kind, "preview curve kind");
                Equal(150d, PreviewCompiler.Evaluate(openingRamp, openingRamp.StartMs + 475), "preview midpoint");

                var selectiveSheet = new CueSheet
                {
                    Name = "Selective",
                    DurationMs = plan.Duration,
                    Defaults = new CuePatternDefinition
                    {
                        Pan = PanPattern.OpenFan,
                        Tilt = TiltPattern.Rise,
                        Dimmer = DimmerPattern.All100,
                        ShutterOpen = true,
                    },
                    Sections =
                    [
                        new CueSheetSection
                        {
                            Name = "Selected heads",
                            StartMs = 0,
                            EndMs = plan.Duration,
                            Heads = [1, 4],
                        },
                    ],
                };
                selectiveSheet.Validate("memory");
                var selective = CueSheetComposer.Apply(plan, selectiveSheet, 4, new FixtureProfileSettings());
                EqualSequence(["D100", "D0", "D0", "D100"], selective.DimmerCues[0].Keys, "cue-sheet fixture selection");
                Throws<InvalidDataException>(
                    () => CueSheetComposer.Apply(
                        plan,
                        selectiveSheet with
                        {
                            Sections = [selectiveSheet.Sections[0] with { Heads = [5] }],
                        },
                        4,
                        new FixtureProfileSettings()),
                    "cue-sheet fixture range");

                Equal("P170_150", CueCurveSlicer.SliceKey("P170_130", 0, 1000, 0, 500),
                    "linear curve first slice");
                Equal("P150_130", CueCurveSlicer.SliceKey("P170_130", 0, 1000, 500, 1000),
                    "linear curve second slice");
                Equal("PB130_210_170", CueCurveSlicer.SliceKey("PB130_210_130", 0, 1000, 0, 750),
                    "bounce curve spanning midpoint");

                var rhythmicSheet = new CueSheet
                {
                    Name = "Rhythmic",
                    DurationMs = 2500,
                    Defaults = new CuePatternDefinition
                    {
                        Pan = PanPattern.Fan,
                        Tilt = TiltPattern.High,
                        Dimmer = DimmerPattern.All100,
                        Rhythm = RhythmPattern.TiltBounceEveryTwoBeats,
                        ShutterOpen = true,
                    },
                    Sections =
                    [
                        new CueSheetSection { Name = "First dimmer slice", StartMs = 0, EndMs = 300 },
                        new CueSheetSection { Name = "Second dimmer slice", StartMs = 300, EndMs = 1500 },
                        new CueSheetSection { Name = "Third dimmer slice", StartMs = 1500, EndMs = 2500 },
                    ],
                };
                var rhythmic = CueSheetComposer.Apply(
                    plan with { Duration = 2500 },
                    rhythmicSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200, 1800, 2400]);
                Equal(3, rhythmic.TiltCues.Count, "rhythm run cue count");
                EqualSequence(["TB85_50_85", "TB100_50_100", "TB100_50_100", "TB85_50_85"],
                    rhythmic.TiltCues[0].Keys, "rhythm first complete cycle");
                EqualSequence(["TB85_50_85", "TB100_50_100", "TB100_50_100", "TB85_50_85"],
                    rhythmic.TiltCues[1].Keys, "rhythm second complete cycle");
                EqualSequence(["T85", "T100", "T100", "T85"],
                    rhythmic.TiltCues[2].Keys, "rhythm incomplete tail settles high");
                EqualSequence(
                    ["0-1200", "1200-2400", "2400-2500"],
                    rhythmic.TiltCues.Select(cue => $"{cue.Start}-{cue.End}"),
                    "rhythm run ignores dimmer boundaries");

                var oddEvenSheet = rhythmicSheet with
                {
                    DurationMs = 1200,
                    Defaults = rhythmicSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltOddEvenEveryBeat,
                    },
                    Sections =
                    [
                        new CueSheetSection { Name = "Odd/even", StartMs = 0, EndMs = 1200 },
                    ],
                };
                var oddEven = CueSheetComposer.Apply(
                    plan with { Duration = 1200 },
                    oddEvenSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200]);
                EqualSequence(["TB85_50_85", "T100", "TB100_50_100", "T85"],
                    oddEven.TiltCues[0].Keys, "odd heads move first");
                EqualSequence(["T85", "TB100_50_100", "T100", "TB85_50_85"],
                    oddEven.TiltCues[1].Keys, "even heads move second");

                var centerOuterSheet = oddEvenSheet with
                {
                    Defaults = oddEvenSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltCenterOuterEveryBeat,
                    },
                };
                var centerOuter = CueSheetComposer.Apply(
                    plan with { Duration = 1200 },
                    centerOuterSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200]);
                EqualSequence(["TB85_50_85", "T100", "T100", "TB85_50_85"],
                    centerOuter.TiltCues[0].Keys, "outer heads move first");
                EqualSequence(["T85", "TB100_50_100", "TB100_50_100", "T85"],
                    centerOuter.TiltCues[1].Keys, "center heads move second");

                var headChaseSheet = oddEvenSheet with
                {
                    DurationMs = 2400,
                    Defaults = oddEvenSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltHeadChaseEveryBeat,
                    },
                    Sections =
                    [
                        new CueSheetSection { Name = "Head chase", StartMs = 0, EndMs = 2400 },
                    ],
                };
                var headChase = CueSheetComposer.Apply(
                    plan with { Duration = 2400 },
                    headChaseSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200, 1800, 2400]);
                Equal(4, headChase.TiltCues.Count, "head chase cycle count");
                for (var cycle = 0; cycle < 4; cycle++)
                {
                    EqualSequence(
                        Enumerable.Range(0, 4).Select(index => index == cycle
                            ? new[] { "TB85_50_85", "TB100_50_100", "TB100_50_100", "TB85_50_85" }[index]
                            : new[] { "T85", "T100", "T100", "T85" }[index]),
                        headChase.TiltCues[cycle].Keys,
                        $"head chase cycle {cycle + 1}");
                    EqualSequence(
                        Enumerable.Range(0, 4).Select(index => index == cycle ? "D100" : "D0"),
                        headChase.DimmerCues[cycle].Keys,
                        $"head chase dimmer cycle {cycle + 1}");
                }

                var pairChaseSheet = oddEvenSheet with
                {
                    DurationMs = 1800,
                    Defaults = oddEvenSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltPairChaseEveryBeat,
                    },
                    Sections =
                    [
                        new CueSheetSection { Name = "Pair chase", StartMs = 0, EndMs = 1800 },
                    ],
                };
                var pairChase = CueSheetComposer.Apply(
                    plan with { Duration = 1800 },
                    pairChaseSheet,
                    5,
                    new FixtureProfileSettings(),
                    [0, 600, 1200, 1800]);
                EqualSequence(["TB85_50_85", "T93", "T100", "T93", "TB85_50_85"],
                    pairChase.TiltCues[0].Keys, "pair chase outer fixtures");
                EqualSequence(["D100", "D0", "D0", "D0", "D100"],
                    pairChase.DimmerCues[0].Keys, "pair chase outer dimmer");
                EqualSequence(["T85", "TB93_50_93", "T100", "TB93_50_93", "T85"],
                    pairChase.TiltCues[1].Keys, "pair chase inner fixtures");
                EqualSequence(["D0", "D100", "D0", "D100", "D0"],
                    pairChase.DimmerCues[1].Keys, "pair chase inner dimmer");
                EqualSequence(["T85", "T93", "TB100_50_100", "T93", "T85"],
                    pairChase.TiltCues[2].Keys, "pair chase center fixture");
                EqualSequence(["D0", "D0", "D100", "D0", "D0"],
                    pairChase.DimmerCues[2].Keys, "pair chase center dimmer");

                foreach (var chaseHeadCount in Enumerable.Range(2, 11))
                {
                    var touchedHeads = Enumerable.Range(0, chaseHeadCount)
                        .SelectMany(cycle => PatternFactory.TiltHeadChase(
                                chaseHeadCount,
                                cycle,
                                new FixtureProfileSettings())
                            .Select((key, index) => new { key, index }))
                        .Where(item => item.key.StartsWith("TB", StringComparison.Ordinal))
                        .Select(item => item.index)
                        .Distinct()
                        .Order()
                        .ToArray();
                    EqualSequence(Enumerable.Range(0, chaseHeadCount), touchedHeads,
                        $"{chaseHeadCount}-head individual chase coverage");

                    var pairCount = (chaseHeadCount + 1) / 2;
                    var touchedPairs = Enumerable.Range(0, pairCount)
                        .SelectMany(cycle => PatternFactory.TiltPairChase(
                                chaseHeadCount,
                                cycle,
                                new FixtureProfileSettings())
                            .Select((key, index) => new { key, index }))
                        .Where(item => item.key.StartsWith("TB", StringComparison.Ordinal))
                        .Select(item => item.index)
                        .Distinct()
                        .Order()
                        .ToArray();
                    EqualSequence(Enumerable.Range(0, chaseHeadCount), touchedPairs,
                        $"{chaseHeadCount}-head pair chase coverage");
                }
                EqualSequence(["PB210_130_210", "PB190_150_190", "PB150_190_150", "PB130_210_130"],
                    PatternFactory.PanRhythmBounce(
                        PanPattern.OppositeFan,
                        4,
                        new FixtureProfileSettings()),
                    "opposite fan pan rhythm returns to its anchor");
                EqualSequence(["TB85_38_85", "TB100_38_100", "TB100_38_100", "TB85_38_85"],
                    PatternFactory.Tilt(
                        TiltPattern.InverseBounce,
                        4,
                        new FixtureProfileSettings(),
                        MotionEnergy.Extended),
                    "extended tilt rhythm reaches calibrated minimum");
                Equal("TCPL85_50_85", MotionCurveKey.ApplyStyle(
                    "TB85_50_85",
                    MotionShape.Punch,
                    MotionPhase.LeftToRight,
                    3,
                    4), "punch ripple key");

                var expressiveSheet = oddEvenSheet with
                {
                    Defaults = oddEvenSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltBounceEveryTwoBeats,
                        MotionEnergy = MotionEnergy.Extended,
                        MotionShape = MotionShape.Punch,
                        MotionPhase = MotionPhase.LeftToRight,
                        IntensityEnvelope = IntensityEnvelope.Turnaround,
                    },
                };
                var expressive = CueSheetComposer.Apply(
                    plan with { Duration = 1200 },
                    expressiveSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200]);
                EqualSequence(
                    ["TCPA85_38_85", "TCPE100_38_100", "TCPH100_38_100", "TCPL85_38_85"],
                    expressive.TiltCues[0].Keys,
                    "punch ripple uses per-fixture phase");
                True(expressive.Definitions.Items.Any(item =>
                        item.Key == "TCPL85_38_85" && item.Value.Contains("Type=Custom", StringComparison.Ordinal)),
                    "punch ripple emits xLights custom curve");
                var expressivePreview = PreviewCompiler.Compile(
                    expressive,
                    4,
                    new FixtureProfileSettings());
                Equal(PreviewCurveKind.Custom, expressivePreview.Fixtures[3].Tilt[0].Kind,
                    "punch ripple preview curve kind");
                Equal(5, expressivePreview.Fixtures[3].Tilt[0].Points!.Count,
                    "punch ripple preview points");
                EqualSequence(["0-450", "450-600", "600-750", "750-1200"],
                    expressive.DimmerCues.Select(cue => $"{cue.Start}-{cue.End}"),
                    "turnaround dimmer timing");
                EqualSequence(["D20", "D20_100", "D100_20", "D20"],
                    expressive.DimmerCues.Select(cue => cue.Keys[0]),
                    "turnaround dimmer envelope");

                var sourceCurveSheet = oddEvenSheet with
                {
                    DurationMs = 1400,
                    Defaults = oddEvenSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltBounceEveryTwoBeats,
                        IntensityEnvelope = IntensityEnvelope.Flash,
                    },
                    Sections =
                    [
                        new CueSheetSection { Name = "Source dimmer curve", StartMs = 0, EndMs = 1400 },
                    ],
                };
                var flash = CueSheetComposer.Apply(
                    plan with { Duration = 1400, FrameIntervalMs = 1 },
                    sourceCurveSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 700, 1400]);
                EqualSequence(["0-467", "467-933", "933-1400"],
                    flash.DimmerCues.Select(cue => $"{cue.Start}-{cue.End}"),
                    "source flash timing");
                EqualSequence(["D0", "D0_100", "D100_0"],
                    flash.DimmerCues.Select(cue => cue.Keys[0]),
                    "source flash envelope");

                var riseAndHold = CueSheetComposer.Apply(
                    plan with { Duration = 1400, FrameIntervalMs = 1 },
                    sourceCurveSheet with
                    {
                        Defaults = sourceCurveSheet.Defaults with
                        {
                            IntensityEnvelope = IntensityEnvelope.RiseAndHold,
                        },
                    },
                    4,
                    new FixtureProfileSettings(),
                    [0, 700, 1400]);
                EqualSequence(["D0_100", "D100", "D100"],
                    riseAndHold.DimmerCues.Select(cue => cue.Keys[0]),
                    "source rise-and-hold envelope");

                var textured = CueSheetComposer.Apply(
                    plan with { Duration = 1400, FrameIntervalMs = 1 },
                    sourceCurveSheet with
                    {
                        Defaults = sourceCurveSheet.Defaults with
                        {
                            IntensityEnvelope = IntensityEnvelope.TexturedPulse,
                        },
                    },
                    4,
                    new FixtureProfileSettings(),
                    [0, 700, 1400]);
                EqualSequence(
                    ["D0", "D0_8", "D8_100", "D100_61", "D61_100", "D100", "D100_0"],
                    textured.DimmerCues.Select(cue => cue.Keys[0]),
                    "source textured pulse envelope");

                var halfBeatSheet = oddEvenSheet with
                {
                    Defaults = oddEvenSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltPulseEveryHalfBeat,
                    },
                };
                var halfBeat = CueSheetComposer.Apply(
                    plan with { Duration = 1200 },
                    halfBeatSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200]);
                EqualSequence(["0-300", "300-600", "600-900", "900-1200"],
                    halfBeat.TiltCues.Select(cue => $"{cue.Start}-{cue.End}"),
                    "half-beat pulse timing");

                var syncopatedSheet = halfBeatSheet with
                {
                    Defaults = halfBeatSheet.Defaults with
                    {
                        Rhythm = RhythmPattern.TiltSyncopatedPulse,
                    },
                };
                var syncopated = CueSheetComposer.Apply(
                    plan with { Duration = 1200 },
                    syncopatedSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200]);
                EqualSequence(["0-300", "300-900", "900-1200"],
                    syncopated.TiltCues.Select(cue => $"{cue.Start}-{cue.End}"),
                    "syncopated pulse timing");

                var boundedTransitionSheet = new CueSheet
                {
                    Name = "Bounded transition",
                    DurationMs = 5000,
                    Defaults = new CuePatternDefinition
                    {
                        Pan = PanPattern.OpenFan,
                        Tilt = TiltPattern.Rise,
                        Dimmer = DimmerPattern.All70,
                        Rhythm = RhythmPattern.Off,
                        ShutterOpen = true,
                    },
                    Sections =
                    [
                        new CueSheetSection { Name = "Long move", StartMs = 0, EndMs = 5000 },
                    ],
                };
                var boundedTransition = CueSheetComposer.Apply(
                    plan with { Duration = 5000 },
                    boundedTransitionSheet,
                    4,
                    new FixtureProfileSettings(),
                    [0, 600, 1200, 1800, 2400, 3000, 3600, 4200, 4800]);
                EqualSequence(["0-2400", "2400-5000"],
                    boundedTransition.PanCues.Select(cue => $"{cue.Start}-{cue.End}"),
                    "long pan transition completes within four beats");
                EqualSequence(["P130", "P150", "P190", "P210"],
                    boundedTransition.PanCues[1].Keys,
                    "long pan transition holds destination");
                EqualSequence(["T85", "T100", "T100", "T85"],
                    boundedTransition.TiltCues[1].Keys,
                    "long tilt transition holds destination");
            }

    private static int DynamicCoverage(IEnumerable<Cue> cues) =>
        cues.Where(cue => cue.Keys.Any(key => key.Contains('_')))
            .Sum(cue => cue.End - cue.Start);

    private static string FormatInterval(RootEffectEvent effect) => $"{effect.Start}-{effect.End}";

    private static void VerifyValidation()
    {
        var settings = new GenerationSettings().Resolve(null, null, null, null, null, null, null, null);
        var validPlan = LilJonSequenceDefinition.Create(4);
        var valid = SequencePlanValidator.Validate(validPlan, settings, null);
        True(!valid.HasErrors(treatWarningsAsErrors: false), "valid plan safety");

        var unsafeCue = Cue.Create(0, validPlan.PanCues[0].End, ["P0_255", "P0_255", "P0_255", "P0_255"], 4);
        var unsafePlan = validPlan with { PanCues = [unsafeCue, .. validPlan.PanCues.Skip(1)] };
        var unsafeReport = SequencePlanValidator.Validate(unsafePlan, settings, null);
        True(
            unsafeReport.Issues.Any(issue => issue.Code is "unsafe-pan-range" or "unsafe-pan-travel"),
            "unsafe motion rejection");

        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "song.xsq");
        var reportPath = Path.Combine(temporary.Path, "validation.json");
        File.WriteAllText(sourcePath, "<xsequence><head><song>A Christmas Storm</song></head></xsequence>");
        var output = new StringWriter();
        var exitCode = new SequencerApplication(output).Run(CommandLineOptions.Parse(
            ["--sequence-path", sourcePath, "--dry-run", "--validation-report", reportPath]));
        Equal(0, exitCode, "dry-run exit code");
        True(File.Exists(reportPath), "dry-run validation report");
        True(!File.Exists(Path.Combine(temporary.Path, "song-4MH.xsq")), "dry-run does not write sequence");

        var bundledReportPath = Path.Combine(temporary.Path, "bundled-validation.json");
        var workspaceRoot = Path.GetDirectoryName(FindRepositoryFile("A-Christmas-Storm(old_layout).xsq"))!;
        exitCode = new SequencerApplication(output).Run(CommandLineOptions.Parse(
            ["--workspace-root", workspaceRoot, "--target", "All", "--dry-run", "--validation-report", bundledReportPath]));
        Equal(0, exitCode, "bundled dry-run exit code");
        using var report = JsonDocument.Parse(File.ReadAllText(bundledReportPath));
        var lilJonOutput = report.RootElement[1].GetProperty("Output").GetString();
        Equal(
            Path.Combine(workspaceRoot, "All-I-Really-Want-For-Christmas-4MH.xsq"),
            lilJonOutput,
            "bundled Lil Jon output compatibility");
    }

    private static void VerifyTimingMaps()
    {
        var storm = XsqTimingMapReader.Read(FindRepositoryFile("A-Christmas-Storm(old_layout).xsq"));
        Equal("Beats", storm.DefaultBeatSource, "Storm default beat source");
        Equal(519, storm.BeatSources.Single(source => source.TrackName == "Beats").Beats.Count, "Storm beat count");
        Equal(141.18, storm.BeatSources.Single(source => source.TrackName == "Beats").Bpm, "Storm BPM");
        True(storm.BeatSources.Single(source => source.TrackName == "Beats").Beats.Any(beat => beat.IsDownbeat),
            "Storm downbeats");
        True(storm.Phrases.Any(marker => marker.TrackName == "Colors" && marker.Kind == TimingTrackKind.Section),
            "Storm section timing");

        var lilJon = XsqTimingMapReader.Read(FindRepositoryFile("All I Really Want For Christmas (feat.xsq"));
        Equal("Beats", lilJon.DefaultBeatSource, "Lil Jon default beat source");
        Equal(100d, lilJon.BeatSources.Single(source => source.TrackName == "Beats").Bpm, "Lil Jon BPM");
        Equal(2, lilJon.BeatSources.Count, "Lil Jon beat source count");
        True(lilJon.Phrases.Any(marker => marker.TrackName == "Lil Jon" && marker.Kind == TimingTrackKind.Phrase),
            "Lil Jon phrase timing");
        True(lilJon.Tracks.Single(track => track.Name == "Lil Jon").Layers.Count == 3,
            "Lil Jon timing hierarchy");
    }

    private static void VerifyWebEditor()
    {
        using var temporary = new TemporaryDirectory();
        var lilJonSourcePath = Path.Combine(temporary.Path, "All I Really Want For Christmas (feat.xsq");
        File.Copy(
            FindRepositoryFile("All I Really Want For Christmas (feat.xsq"),
            lilJonSourcePath);
        var originalLilJonBytes = File.ReadAllBytes(lilJonSourcePath);
        var genericSourcePath = Path.Combine(temporary.Path, "1950.xsq");
        File.Copy(FindRepositoryFile("1950.xsq"), genericSourcePath);
        var raveSourcePath = Path.Combine(temporary.Path, "Aronchupa - Rave In The Grave HD Layout.xsq");
        File.Copy(FindRepositoryFile("Aronchupa - Rave In The Grave HD Layout.xsq"), raveSourcePath);
        File.Copy(genericSourcePath, Path.Combine(temporary.Path, "1950-4MH.xsq"));
        File.Copy(genericSourcePath, Path.Combine(temporary.Path, "1950-editor-12MH.xsq"));
        File.WriteAllText(Path.Combine(temporary.Path, "broken.xsq"), "<not-valid");
        var stormSourcePath = Path.Combine(temporary.Path, "A-Christmas-Storm(old_layout).xsq");
        File.Copy(FindRepositoryFile("A-Christmas-Storm(old_layout).xsq"), stormSourcePath);

        var editor = new EditorService(temporary.Path);
        var document = editor.Bootstrap("liljon", 4);
        Equal(temporary.Path, document.WorkspaceRoot, "web editor workspace root");
        Equal(4, document.Sequences.Count, "web editor scanned sequence count");
        True(document.Sequences.Any(sequence => sequence.Id == "file:1950.xsq"),
            "web editor discovered generic sequence");
        Equal(4, document.Preview.Fixtures.Count, "web editor fixture count");
        Equal("All I Really Want For Christmas (feat.xsq", document.SourceFileName,
            "web editor source filename");
        Equal(Convert.ToHexString(SHA256.HashData(originalLilJonBytes)), document.SourceFingerprint,
            "web editor source fingerprint");
        Equal(53, document.CueSheet.Sections.Count, "web editor cue count");
        Equal("Beats", document.Timing.DefaultBeatSource, "web editor timing source");
        Equal(100d, document.Timing.BeatSources.Single(source => source.TrackName == "Beats").Bpm,
            "web editor tempo");
        EqualSequence(
            [
                "Off", "TiltLiftEveryBeat", "TiltPulseEveryHalfBeat",
                "TiltBounceEveryBeat", "TiltBounceEveryTwoBeats", "TiltSyncopatedPulse",
                "TiltOddEvenEveryBeat", "TiltCenterOuterEveryBeat",
                "TiltHeadChaseEveryBeat", "TiltPairChaseEveryBeat",
                "PanBounceEveryTwoBeats",
                "PanTiltBounceEveryFourBeats",
            ],
            document.RhythmPatterns,
            "web editor rhythm choices");
        True(document.Media.RequiresRelink, "web editor media relink status");
        True(document.Validation.Issues.Count == 0, "web editor bootstrap validation");
        Equal(38, document.Validation.Metrics.PanMinimum, "web editor pan minimum metric");
        Equal(212, document.Validation.Metrics.PanMaximum, "web editor pan maximum metric");
        Equal(38, document.Validation.Metrics.TiltMinimum, "web editor tilt minimum metric");
        Equal(123, document.Validation.Metrics.TiltMaximum, "web editor tilt maximum metric");

        using var alternateWorkspace = new TemporaryDirectory();
        File.Copy(
            FindRepositoryFile("All I Really Want For Christmas (feat.xsq"),
            Path.Combine(alternateWorkspace.Path, "All I Really Want For Christmas (feat.xsq"));
        var preferencePath = Path.Combine(temporary.Path, "preferences", "workspace-root.txt");
        var preferenceStore = new WorkspacePreferenceStore(preferencePath);
        var workspaceHost = new EditorWorkspaceHost(temporary.Path, preferenceStore);
        var switched = workspaceHost.Switch(new WorkspaceSwitchRequest(alternateWorkspace.Path, 4));
        Equal(Path.GetFullPath(alternateWorkspace.Path), switched.WorkspaceRoot,
            "workspace switch root");
        Equal(Path.GetFullPath(alternateWorkspace.Path), preferenceStore.Load(),
            "workspace preference persisted");
        Equal(Path.GetFullPath(alternateWorkspace.Path), workspaceHost.Bootstrap(null, 4).WorkspaceRoot,
            "workspace host delegates to replacement service");
        Throws<InvalidDataException>(
            () => workspaceHost.Switch(new WorkspaceSwitchRequest("relative-path", 4)),
            "relative workspace rejected");
        using var emptyWorkspace = new TemporaryDirectory();
        var emptyPreferenceStore = new WorkspacePreferenceStore(
            Path.Combine(temporary.Path, "empty-preferences", "workspace-root.txt"));
        var emptyWorkspaceHost = new EditorWorkspaceHost(emptyWorkspace.Path, emptyPreferenceStore);
        Equal(Path.GetFullPath(emptyWorkspace.Path), emptyWorkspaceHost.WorkspaceRoot,
            "empty workspace host starts");
        Throws<InvalidDataException>(
            () => emptyWorkspaceHost.Bootstrap(null, 4),
            "empty workspace bootstrap requests sequence folder");
        Equal(Path.GetFullPath(alternateWorkspace.Path),
            emptyWorkspaceHost.Switch(new WorkspaceSwitchRequest(alternateWorkspace.Path, 4)).WorkspaceRoot,
            "empty workspace host recovers through valid switch");
        Throws<InvalidDataException>(
            () => workspaceHost.Switch(new WorkspaceSwitchRequest(emptyWorkspace.Path, 4)),
            "workspace without valid sequences rejected");
        Equal(Path.GetFullPath(alternateWorkspace.Path), workspaceHost.WorkspaceRoot,
            "failed switch preserves active workspace");

        var savedProject = editor.SaveProject(new SaveStudioProjectRequest(
            new EditorRequest(
                "liljon",
                4,
                document.CueSheet,
                document.FixtureProfile,
                document.SuggestedOutputFileName,
                TimingSourceName: "Beats"),
            document.SourceFingerprint,
            null,
            new StudioProjectPreview("floor", [0, 90, 180, 270]),
            new StudioProjectView(16, 28875),
            new StudioProjectRegeneration(null, null)));
        var loadedProject = editor.LoadProject("liljon", 4).Project;
        True(loadedProject is not null, "hidden project saved and loaded");
        Equal(SequenceProjectStore.ProjectFormat, loadedProject!.Format,
            "hidden project format");
        Equal(SequenceProjectStore.ProjectVersion, loadedProject.Version,
            "hidden project version");
        Equal(540, loadedProject.Editor.FixtureProfile.PanTravelDegrees,
            "hidden project pan travel degrees");
        Equal(270, loadedProject.Editor.FixtureProfile.TiltTravelDegrees,
            "hidden project tilt travel degrees");
        var initialProjectPath = Directory.EnumerateFiles(
                Path.Combine(temporary.Path, ".moving-head-sequencer", "projects"),
                "4.mhproj",
                SearchOption.AllDirectories)
            .Single();
        var legacyProjectJson = JsonNode.Parse(File.ReadAllText(initialProjectPath))!.AsObject();
        var legacyFixtureProfile = legacyProjectJson["editor"]!["fixtureProfile"]!.AsObject();
        legacyFixtureProfile.Remove("panTravelDegrees");
        legacyFixtureProfile.Remove("tiltTravelDegrees");
        File.WriteAllText(
            initialProjectPath,
            legacyProjectJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var legacyLoadedProject = editor.LoadProject("liljon", 4).Project;
        True(legacyLoadedProject is not null, "legacy hidden project without travel degrees loads");
        Equal(540, legacyLoadedProject!.Editor.FixtureProfile.PanTravelDegrees,
            "legacy hidden project defaults pan travel degrees");
        Equal(270, legacyLoadedProject.Editor.FixtureProfile.TiltTravelDegrees,
            "legacy hidden project defaults tilt travel degrees");
        Equal(savedProject.SourceFingerprint, loadedProject.Source.Fingerprint,
            "hidden project source fingerprint");
        Equal(savedProject.ProjectRevision, loadedProject.Revision,
            "hidden project revision returned to client");
        Equal(document.CueSheet.Sections.Count, loadedProject.Editor.CueSheet.Sections.Count,
            "hidden project cue count");
        EqualSequence([0, 90, 180, 270], loadedProject.Preview.FixtureRotations,
            "hidden project preview rotations");
        Equal(16, loadedProject.View.SelectedCueIndex, "hidden project selected cue");
        True(editor.LoadProject("liljon", 5).Project is null,
            "hidden projects are isolated by head count");

        var revisedProject = editor.SaveProject(new SaveStudioProjectRequest(
            new EditorRequest(
                "liljon",
                4,
                document.CueSheet with { Name = "Revised hidden project" },
                document.FixtureProfile,
                document.SuggestedOutputFileName,
                TimingSourceName: "Beats"),
            document.SourceFingerprint,
            loadedProject.Revision,
            new StudioProjectPreview("truss", [270, 180, 90, 0]),
            new StudioProjectView(3, 12100),
            new StudioProjectRegeneration(null, null)));
        Equal("Revised hidden project", editor.LoadProject("liljon", 4).Project!.Editor.CueSheet.Name,
            "hidden project revision replaces active project");
        True(Directory.EnumerateFiles(
                Path.Combine(temporary.Path, ".moving-head-sequencer", "projects"),
                "*.mhproj",
                SearchOption.AllDirectories).Count() >= 2,
            "hidden project revision archived");
        Throws<IOException>(
            () => editor.SaveProject(new SaveStudioProjectRequest(
                new EditorRequest(
                    "liljon",
                    4,
                    document.CueSheet,
                    document.FixtureProfile,
                    document.SuggestedOutputFileName,
                    TimingSourceName: "Beats"),
                document.SourceFingerprint,
                loadedProject.Revision,
                new StudioProjectPreview("floor", [0, 0, 0, 0]),
                new StudioProjectView(0, 0),
                new StudioProjectRegeneration(null, null))),
            "stale editor tab project save rejected");

        File.AppendAllText(lilJonSourcePath, " ");
        var staleProject = editor.LoadProject("liljon", 4).Project!;
        True(staleProject.Source.Fingerprint !=
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lilJonSourcePath))),
            "hidden project detects changed source fingerprint");
        Throws<IOException>(
            () => editor.SaveProject(new SaveStudioProjectRequest(
                new EditorRequest(
                    "liljon",
                    4,
                    document.CueSheet,
                    document.FixtureProfile,
                    document.SuggestedOutputFileName,
                    TimingSourceName: "Beats"),
                document.SourceFingerprint,
                revisedProject.ProjectRevision,
                new StudioProjectPreview("floor", [0, 0, 0, 0]),
                new StudioProjectView(0, 0),
                new StudioProjectRegeneration(null, null))),
            "stale browser project autosave rejected");
        Throws<IOException>(
            () => editor.Compile(new EditorRequest(
                "liljon",
                4,
                document.CueSheet,
                document.FixtureProfile,
                TimingSourceName: "Beats",
                ExpectedSourceFingerprint: document.SourceFingerprint)),
            "stale browser preview rejected");
        File.WriteAllBytes(lilJonSourcePath, originalLilJonBytes);
        editor.ResetProject(new ResetStudioProjectRequest("liljon", 4));
        True(editor.LoadProject("liljon", 4).Project is null,
            "hidden project reset archives active project");

        editor.SaveProject(new SaveStudioProjectRequest(
            new EditorRequest(
                "liljon",
                4,
                document.CueSheet,
                document.FixtureProfile,
                document.SuggestedOutputFileName,
                TimingSourceName: "Beats"),
            document.SourceFingerprint,
            null,
            new StudioProjectPreview("floor", [0, 0, 0, 0]),
            new StudioProjectView(0, 0),
            new StudioProjectRegeneration(null, null)));
        var activeProjectPath = Directory.EnumerateFiles(
                Path.Combine(temporary.Path, ".moving-head-sequencer", "projects"),
                "4.mhproj",
                SearchOption.AllDirectories)
            .Single();
        File.WriteAllText(activeProjectPath, "{not-json");
        var corruptLoad = editor.LoadProject("liljon", 4);
        True(corruptLoad.Project is null && corruptLoad.Warning is not null,
            "corrupt hidden project archived with warning");
        True(!File.Exists(activeProjectPath), "corrupt active project no longer blocks sequence");

        var partyCueIndex = document.CueSheet.Sections
            .Select((section, index) => new { section, index })
            .Single(item => item.section.StartMs == 28875)
            .index;
        var suggestionRequest = new EditorRequest(
            "liljon",
            4,
            document.CueSheet,
            document.FixtureProfile,
            TimingSourceName: "Beats");
        var suggestions = editor.Suggest(new CueRegenerationRequest(suggestionRequest, partyCueIndex));
        Equal(partyCueIndex, suggestions.CueIndex, "cue regeneration index");
        Equal(4, suggestions.Choices.Count, "cue regeneration choice count");
        True(suggestions.Choices.Any(choice => choice.Id == "rest"), "cue regeneration includes rest");
        True(suggestions.Choices.Any(choice => choice.Id == "hold"), "cue regeneration includes hold");
        True(suggestions.ConcurrentEffects.Count > 0, "cue regeneration concurrent evidence");
        EqualSequence(
            suggestions.Choices.OrderByDescending(choice => choice.Confidence).Select(choice => choice.Id),
            suggestions.Choices.Select(choice => choice.Id),
            "cue regeneration confidence order");
        var topChoice = suggestions.Choices[0];
        var regeneratedSection = document.CueSheet.Sections[partyCueIndex] with
        {
            Pattern = null,
            Pan = topChoice.Pan,
            Tilt = topChoice.Tilt,
            Dimmer = topChoice.Dimmer,
            Rhythm = topChoice.Rhythm,
            ShutterOpen = topChoice.ShutterOpen,
            PanKeys = null,
            TiltKeys = null,
            DimmerKeys = null,
        };
        var regeneratedSections = document.CueSheet.Sections.ToArray();
        regeneratedSections[partyCueIndex] = regeneratedSection;
        var regenerated = editor.Compile(suggestionRequest with
        {
            CueSheet = document.CueSheet with { Sections = regeneratedSections },
        });
        True(!regenerated.Validation.HasErrors(treatWarningsAsErrors: false),
            "cue regeneration compiles safely");
        Equal(28875, regenerated.CueSheet.Sections[partyCueIndex].StartMs,
            "cue regeneration preserves start");
        Equal(33675, regenerated.CueSheet.Sections[partyCueIndex].EndMs,
            "cue regeneration preserves end");
        var wholeSong = editor.Regenerate(new WholeSongRegenerationRequest(suggestionRequest));
        Equal(document.CueSheet.Sections.Count, wholeSong.AppliedChoices.Count,
            "whole-song regeneration applies every cue");
        Equal(document.CueSheet.Sections.Count, wholeSong.Document.CueSheet.Sections.Count,
            "whole-song regeneration preserves cue count");
        True(wholeSong.AverageConfidence > 0,
            "whole-song regeneration reports confidence");
        True(wholeSong.Document.Validation.Issues.Count == 0,
            "whole-song regeneration validates without issues");
        True(wholeSong.Document.Validation.Metrics.BlackoutWindowCount >= 3,
            "whole-song regeneration creates intentional blackout windows");
        True(wholeSong.Document.Validation.Metrics.DimmerVisiblePercent is >= 70 and <= 90,
            "whole-song visibility preserves light/dark contrast");
        True(!wholeSong.AppliedChoices[0].ShutterOpen &&
                !wholeSong.AppliedChoices[^1].ShutterOpen,
            "whole-song regeneration opens and closes in darkness");
        var staticLitDurationMs = wholeSong.AppliedChoices
            .Where(choice => choice.ShutterOpen && choice.Dimmer != DimmerPattern.All0 &&
                choice.Rhythm == RhythmPattern.Off &&
                choice.Pan is PanPattern.Park or PanPattern.Fan or PanPattern.OppositeFan &&
                choice.Tilt is TiltPattern.Park or TiltPattern.High)
            .Sum(choice =>
            {
                var section = wholeSong.Document.CueSheet.Sections[choice.CueIndex];
                return section.EndMs - section.StartMs;
            });
        True(staticLitDurationMs <= 1000,
            "whole-song regeneration minimizes static lit passages");
        var longestDynamicSegmentMs = wholeSong.Document.Preview.Fixtures
            .SelectMany(fixture => fixture.Pan.Concat(fixture.Tilt))
            .Where(segment => segment.Kind != PreviewCurveKind.Hold)
            .Select(segment => segment.EndMs - segment.StartMs)
            .DefaultIfEmpty()
            .Max();
        True(longestDynamicSegmentMs <= 2400,
            "whole-song position transitions complete within four beats");
        True(wholeSong.AppliedChoices.Count(choice => choice.Rhythm != RhythmPattern.Off) >= 12,
            "whole-song regeneration keeps substantial beat-driven coverage");
        True(wholeSong.AppliedChoices
                .Where(choice => choice.Rhythm != RhythmPattern.Off)
                .Select(choice => choice.Rhythm)
                .Distinct()
                .Count() >= 5,
            "whole-song regeneration uses multiple rhythm families");
        True(wholeSong.AppliedChoices.Any(choice =>
                choice.Rhythm == RhythmPattern.TiltOddEvenEveryBeat),
            "whole-song regeneration includes odd/even fixture movement");
        True(wholeSong.AppliedChoices.Any(choice =>
                choice.Rhythm == RhythmPattern.TiltCenterOuterEveryBeat),
            "whole-song regeneration includes center/outer fixture movement");
        True(wholeSong.AppliedChoices.Any(choice =>
                choice.Rhythm == RhythmPattern.TiltLiftEveryBeat),
            "whole-song regeneration includes low-position beat lifts");
        True(wholeSong.AppliedChoices.Any(choice =>
                choice.Rhythm == RhythmPattern.TiltHeadChaseEveryBeat),
            "whole-song regeneration includes per-head chase movement");
        True(wholeSong.AppliedChoices.Any(choice =>
                choice.Rhythm == RhythmPattern.TiltPairChaseEveryBeat),
            "whole-song regeneration includes mirrored-pair chase movement");
        foreach (var chaseRhythm in new[]
                 {
                     RhythmPattern.TiltHeadChaseEveryBeat,
                     RhythmPattern.TiltPairChaseEveryBeat,
                 })
        {
            var chaseIntervals = wholeSong.AppliedChoices
                .Where(choice => choice.Rhythm == chaseRhythm)
                .Select(choice => wholeSong.Document.CueSheet.Sections[choice.CueIndex])
                .Select(section => (section.StartMs, section.EndMs))
                .ToArray();
            var participatingFixtures = wholeSong.Document.Preview.Fixtures
                .Where(fixture => fixture.Tilt.Any(segment =>
                    segment.Kind == PreviewCurveKind.Bounce &&
                    chaseIntervals.Any(interval =>
                        segment.StartMs < interval.EndMs && segment.EndMs > interval.StartMs)))
                .Select(fixture => fixture.Number);
            EqualSequence(
                Enumerable.Range(1, wholeSong.Document.HeadCount),
                participatingFixtures,
                $"whole-song {chaseRhythm} fixture participation");
        }
        var maximumRhythmStreak = 0;
        var currentRhythmStreak = 0;
        RhythmPattern? previousRhythm = null;
        foreach (var applied in wholeSong.AppliedChoices)
        {
            if (applied.Rhythm == RhythmPattern.Off)
            {
                previousRhythm = null;
                currentRhythmStreak = 0;
                continue;
            }
            currentRhythmStreak = applied.Rhythm == previousRhythm
                ? currentRhythmStreak + 1
                : 1;
            previousRhythm = applied.Rhythm;
            maximumRhythmStreak = Math.Max(maximumRhythmStreak, currentRhythmStreak);
        }
        True(maximumRhythmStreak <= 2,
            "whole-song regeneration limits rhythm-family repetition");
        True(wholeSong.AppliedChoices.Any(choice => choice.Rhythm is
                RhythmPattern.PanBounceEveryTwoBeats or
                RhythmPattern.PanTiltBounceEveryFourBeats),
            "whole-song regeneration includes rhythmic pan movement");
        True(wholeSong.AppliedChoices.Any(choice =>
                choice.Rhythm == RhythmPattern.PanTiltBounceEveryFourBeats),
            "whole-song regeneration includes combined pan and tilt motion");
        True(wholeSong.AppliedChoices.Any(choice => choice.MotionEnergy == MotionEnergy.Extended),
            "whole-song regeneration reserves extended range for impact");
        True(wholeSong.AppliedChoices.Any(choice => choice.MotionShape == MotionShape.Punch),
            "whole-song regeneration includes punch motion");
        True(wholeSong.AppliedChoices.Any(choice => choice.MotionShape == MotionShape.Double),
            "whole-song regeneration includes double-hit motion");
        True(wholeSong.AppliedChoices.Any(choice => choice.MotionPhase != MotionPhase.Together),
            "whole-song regeneration includes fixture phase offsets");
        True(wholeSong.AppliedChoices.All(choice =>
                choice.Dimmer is DimmerPattern.All0 or DimmerPattern.All100),
            "whole-song regeneration uses only off/full base dimmers");
        foreach (var envelope in new[]
                 {
                     IntensityEnvelope.Pulse,
                     IntensityEnvelope.Flash,
                     IntensityEnvelope.RiseAndHold,
                     IntensityEnvelope.TexturedPulse,
                 })
        {
            True(wholeSong.AppliedChoices.Any(choice => choice.IntensityEnvelope == envelope),
                $"whole-song regeneration includes {envelope} dimmer curves");
        }
        True(wholeSong.AppliedChoices.Any(choice => choice.Rhythm is
                RhythmPattern.TiltPulseEveryHalfBeat or RhythmPattern.TiltSyncopatedPulse),
            "whole-song regeneration includes varied rhythmic subdivision");
        True(wholeSong.Document.Validation.Metrics.TiltDynamicPercent is >= 50 and <= 70,
            "whole-song tilt motion is lively and bounded");
        True(wholeSong.Document.Preview.Fixtures.All(fixture =>
                fixture.Tilt.Count(segment => segment.Kind == PreviewCurveKind.Bounce) >= 12),
            "whole-song regeneration gives every fixture repeated tilt bounces");
        foreach (var applied in wholeSong.AppliedChoices)
        {
            var resolved = wholeSong.Document.CueSheet.Resolve(
                wholeSong.Document.CueSheet.Sections[applied.CueIndex]);
            Equal(applied.Pan, resolved.Pan!.Value,
                $"whole-song applied pan matches cue {applied.CueIndex}");
            Equal(applied.Tilt, resolved.Tilt!.Value,
                $"whole-song applied tilt matches cue {applied.CueIndex}");
            Equal(applied.Dimmer, resolved.Dimmer!.Value,
                $"whole-song applied dimmer matches cue {applied.CueIndex}");
            Equal(applied.Rhythm, resolved.Rhythm ?? RhythmPattern.Off,
                $"whole-song applied rhythm matches cue {applied.CueIndex}");
            Equal(applied.MotionEnergy, resolved.MotionEnergy ?? MotionEnergy.Full,
                $"whole-song applied energy matches cue {applied.CueIndex}");
            Equal(applied.MotionShape, resolved.MotionShape ?? MotionShape.Smooth,
                $"whole-song applied shape matches cue {applied.CueIndex}");
            Equal(applied.MotionPhase, resolved.MotionPhase ?? MotionPhase.Together,
                $"whole-song applied phase matches cue {applied.CueIndex}");
            Equal(applied.IntensityEnvelope, resolved.IntensityEnvelope ?? IntensityEnvelope.Steady,
                $"whole-song applied envelope matches cue {applied.CueIndex}");
            Equal(applied.ShutterOpen, resolved.ShutterOpen!.Value,
                $"whole-song applied shutter matches cue {applied.CueIndex}");
        }
        EqualSequence(
            document.CueSheet.Sections.Select(section => (section.StartMs, section.EndMs)),
            wholeSong.Document.CueSheet.Sections.Select(section => (section.StartMs, section.EndMs)),
            "whole-song regeneration preserves cue boundaries");
        var repeatedWholeSong = editor.Regenerate(new WholeSongRegenerationRequest(suggestionRequest));
        Equal(
            JsonSerializer.Serialize(wholeSong),
            JsonSerializer.Serialize(repeatedWholeSong),
            "whole-song regeneration is deterministic");
        var generatedWholeSong = editor.Generate(suggestionRequest with
        {
            CueSheet = wholeSong.Document.CueSheet,
            OutputFileName = "whole-song-diverse.xsq",
        });
        True(File.Exists(generatedWholeSong.OutputPath),
            "whole-song diverse XSQ generated");
        True(!generatedWholeSong.Validation.HasErrors(treatWarningsAsErrors: false),
            "whole-song diverse XSQ validates");

        editor.SaveProject(new SaveStudioProjectRequest(
            suggestionRequest,
            document.SourceFingerprint,
            null,
            new StudioProjectPreview("floor", [0, 0, 0, 0]),
            new StudioProjectView(0, 0),
            new StudioProjectRegeneration(null, null)));
        var originalGeneration = editor.GenerateOriginal(suggestionRequest with
        {
            CueSheet = wholeSong.Document.CueSheet,
            ExpectedSourceFingerprint = document.SourceFingerprint,
        });
        True(originalGeneration.Messages.Contains("Post-write XSQ validation passed."),
            "original web generation reports post-write validation");
        True(File.Exists(lilJonSourcePath), "original generation keeps source path");
        True(!File.ReadAllBytes(lilJonSourcePath).SequenceEqual(originalLilJonBytes),
            "original generation replaces source content");
        var generatedOriginalBytes = File.ReadAllBytes(lilJonSourcePath);
        Equal(Convert.ToHexString(SHA256.HashData(generatedOriginalBytes)),
            originalGeneration.SourceFingerprint,
            "original generation returns replacement fingerprint");
        True(editor.LoadProject("liljon", 4).Project is null,
            "original generation archives linked hidden project");
        var sourceDocument = new XmlDocument();
        sourceDocument.Load(lilJonSourcePath);
        Equal("xsequence", sourceDocument.DocumentElement!.Name,
            "replaced original remains valid XSQ XML");
        var firstCatalog = editor.ListBackups("liljon");
        Equal("All I Really Want For Christmas (feat.xsq", firstCatalog.SourceFileName,
            "backup catalog source filename");
        True(firstCatalog.Backups.Any(backup => backup.Id == originalGeneration.Backup.Id),
            "original backup listed");

        var restoredOriginal = editor.RestoreBackup(new RestoreBackupRequest(
            "liljon",
            originalGeneration.Backup.Id));
        EqualSequence(originalLilJonBytes, File.ReadAllBytes(lilJonSourcePath),
            "restore reproduces exact original bytes");
        True(restoredOriginal.PreviousVersionBackup.Id != originalGeneration.Backup.Id,
            "restore preserves replaced version separately");

        editor.RestoreBackup(new RestoreBackupRequest(
            "liljon",
            restoredOriginal.PreviousVersionBackup.Id));
        EqualSequence(generatedOriginalBytes, File.ReadAllBytes(lilJonSourcePath),
            "restore safety backup recovers generated original");
        editor.RestoreBackup(new RestoreBackupRequest("liljon", originalGeneration.Backup.Id));
        EqualSequence(originalLilJonBytes, File.ReadAllBytes(lilJonSourcePath),
            "original can be restored repeatedly without consuming backup");
        True(editor.ListBackups("liljon").Backups.Count >= 4,
            "each replacement and restore preserves a backup");
        Throws<InvalidDataException>(
            () => editor.RestoreBackup(new RestoreBackupRequest("liljon", "../outside.xsq")),
            "backup traversal rejected");

        var safetyStore = new SequenceBackupStore(temporary.Path);
        var staleSource = Path.Combine(temporary.Path, "stale-source.xsq");
        File.WriteAllText(staleSource, "<xsequence><head /></xsequence>");
        var staleFingerprint = safetyStore.Fingerprint(staleSource);
        File.WriteAllText(staleSource, "<xsequence><changed /></xsequence>");
        var staleStaging = safetyStore.CreateStagingPath();
        File.WriteAllText(staleStaging, "<xsequence><generated /></xsequence>");
        Throws<IOException>(
            () => safetyStore.ReplaceOriginal(staleSource, staleStaging, staleFingerprint),
            "stale original replacement rejected");
        True(File.ReadAllText(staleSource).Contains("changed", StringComparison.Ordinal),
            "stale original remains untouched");
        Equal(0, safetyStore.List(staleSource).Count,
            "stale replacement creates no backup");

        var invalidSource = Path.Combine(temporary.Path, "invalid-staging-source.xsq");
        File.WriteAllText(invalidSource, "<xsequence><head /></xsequence>");
        var invalidFingerprint = safetyStore.Fingerprint(invalidSource);
        var invalidStaging = safetyStore.CreateStagingPath();
        File.WriteAllText(invalidStaging, "<not-xsequence />");
        Throws<InvalidDataException>(
            () => safetyStore.ReplaceOriginal(invalidSource, invalidStaging, invalidFingerprint),
            "invalid staging replacement rejected");
        True(File.ReadAllText(invalidSource).Contains("head", StringComparison.Ordinal),
            "invalid staging leaves original untouched");
        Equal(0, safetyStore.List(invalidSource).Count,
            "invalid staging creates no backup");

        foreach (var headCount in Enumerable.Range(2, 11))
        {
            var countDocument = editor.Bootstrap("liljon", headCount);
            Equal(headCount, countDocument.Preview.Fixtures.Count,
                $"web editor {headCount}-head fixture count");
            True(countDocument.CueSheet.Sections.All(section =>
                    section.PanKeys?.Count == headCount &&
                    section.TiltKeys?.Count == headCount &&
                    section.DimmerKeys?.Count == headCount),
                $"web editor {headCount}-head cue keys");
            True(countDocument.Validation.Issues.Count == 0,
                $"web editor {headCount}-head validation");
            if (headCount is 2 or 5 or 12)
            {
                var countWholeSong = editor.Regenerate(new WholeSongRegenerationRequest(
                    new EditorRequest(
                        "liljon",
                        headCount,
                        countDocument.CueSheet,
                        countDocument.FixtureProfile,
                        TimingSourceName: "Beats")));
                Equal(headCount, countWholeSong.Document.Preview.Fixtures.Count,
                    $"whole-song {headCount}-head fixture count");
                True(countWholeSong.Document.Validation.Issues.Count == 0,
                    $"whole-song {headCount}-head validation");
            }
        }
        var previewSegments = document.Preview.Fixtures
            .SelectMany(fixture => new[] { fixture.Pan, fixture.Tilt })
            .ToArray();
        var maximumBoundaryJump = previewSegments
            .SelectMany(segments => segments.Zip(
                segments.Skip(1),
                (left, right) => Math.Abs(left.EndValue - right.StartValue)))
            .DefaultIfEmpty()
            .Max();
        var maximumPreviewSpeed = previewSegments
            .SelectMany(segments => segments)
            .Where(segment => segment.EndMs > segment.StartMs)
            .Select(segment =>
                1000d * Math.Max(
                    Math.Abs(segment.EndValue - segment.StartValue),
                    segment.MiddleValue is null
                        ? 0
                        : Math.Max(
                            Math.Abs(segment.MiddleValue.Value - segment.StartValue),
                            Math.Abs(segment.EndValue - segment.MiddleValue.Value))) /
                (segment.EndMs - segment.StartMs))
            .DefaultIfEmpty()
            .Max();
        Equal(0d, maximumBoundaryJump, "web editor movement boundary continuity");
        True(maximumPreviewSpeed <= 170, "web editor movement speed bound");

        var rhythmicCueSheet = document.CueSheet with
        {
            Sections = document.CueSheet.Sections.Select(section => section.StartMs == 28875
                ? section with
                {
                    Tilt = TiltPattern.High,
                    Rhythm = RhythmPattern.TiltBounceEveryTwoBeats,
                    TiltKeys = null,
                }
                : section).ToArray(),
        };
        var rhythmicRequest = new EditorRequest(
            "liljon",
            4,
            rhythmicCueSheet,
            document.FixtureProfile,
            "rhythm-web-test.xsq",
            Force: true,
            TimingSourceName: "Beats");
        var rhythmicCompiled = editor.Compile(rhythmicRequest);
        EqualSequence(
            ["28875-30100", "30100-31275", "31275-32500", "32500-33675"],
            rhythmicCompiled.Preview.Fixtures[0].Tilt
                .Where(segment => segment.StartMs >= 28875 && segment.EndMs <= 33675)
                .Select(segment => $"{segment.StartMs}-{segment.EndMs}"),
            "web editor two-beat rhythm cycles");
        True(rhythmicCompiled.Validation.Issues.Count == 0, "web editor rhythmic validation");
        var rhythmicGenerated = editor.Generate(rhythmicRequest);
        True(File.Exists(rhythmicGenerated.OutputPath), "web editor rhythmic generation");

        var fiveHeadDocument = editor.Bootstrap("liljon", 5);
        var fiveHeadRhythmicSheet = fiveHeadDocument.CueSheet with
        {
            Sections = fiveHeadDocument.CueSheet.Sections.Select(section => section.StartMs == 28875
                ? section with
                {
                    Tilt = TiltPattern.High,
                    Rhythm = RhythmPattern.TiltBounceEveryTwoBeats,
                    TiltKeys = null,
                }
                : section).ToArray(),
        };
        var fiveHeadRhythmic = editor.Compile(new EditorRequest(
            "liljon",
            5,
            fiveHeadRhythmicSheet,
            fiveHeadDocument.FixtureProfile,
            TimingSourceName: "Beats"));
        Equal(5, fiveHeadRhythmic.Preview.Fixtures.Count, "five-head rhythmic fixture count");
        True(fiveHeadRhythmic.Preview.Fixtures.All(fixture =>
                fixture.Tilt.Count(segment => segment.StartMs >= 28875 && segment.EndMs <= 33675) == 4),
            "five-head rhythmic cycles");
        True(fiveHeadRhythmic.Validation.Issues.Count == 0, "five-head rhythmic validation");

        var genericSourceBefore = File.ReadAllText(genericSourcePath);
        var genericDocument = editor.Bootstrap("file:1950.xsq", 4);
        Equal("Run Rudolph Run", genericDocument.SequenceName, "generic sequence song title");
        Equal("1950-editor-4MH.xsq", genericDocument.SuggestedOutputFileName,
            "generic sequence suggested output");
        Equal(132900, genericDocument.Preview.DurationMs, "generic sequence source duration");
        Equal(1, genericDocument.CueSheet.Sections.Count, "generic sequence initial cue count");
        Equal(DimmerPattern.All0, genericDocument.CueSheet.Sections[0].Dimmer,
            "generic sequence starts dark");
        True(genericDocument.Validation.Issues.Count == 0, "generic sequence initial validation");
        var genericResult = editor.Generate(new EditorRequest(
            "file:1950.xsq",
            4,
            genericDocument.CueSheet,
            genericDocument.FixtureProfile,
            "1950-web-test.xsq",
            Force: true,
            TimingSourceName: genericDocument.Timing.DefaultBeatSource));
        True(File.Exists(genericResult.OutputPath), "generic sequence generated output");
        True(genericResult.Messages.Contains("Post-write XSQ validation passed."),
            "generic web generation reports post-write validation");
        Equal(genericSourceBefore, File.ReadAllText(genericSourcePath), "generic source remains unchanged");
        var genericOutput = new XmlDocument();
        genericOutput.Load(genericResult.OutputPath);
        True(genericOutput.SelectSingleNode("/xsequence/ElementEffects/Element[@name='MH-Pan']") is not null,
            "generic output contains mover controls");
        foreach (var headCount in new[] { 2, 12 })
        {
            var countDocument = editor.Bootstrap("file:1950.xsq", headCount);
            var countResult = editor.Generate(new EditorRequest(
                "file:1950.xsq",
                headCount,
                countDocument.CueSheet,
                countDocument.FixtureProfile,
                $"1950-{headCount}-head-test.xsq",
                Force: true,
                TimingSourceName: countDocument.Timing.DefaultBeatSource));
            var countOutput = new XmlDocument();
            countOutput.Load(countResult.OutputPath);
            Equal(
                headCount,
                countOutput.SelectNodes(
                    "/xsequence/ElementEffects/Element[@name='MH-Pan']/Strand[@index='0']/Node")?.Count ?? 0,
                $"generic {headCount}-head generated nodes");
        }

        var raveSourceBefore = File.ReadAllText(raveSourcePath);
        var raveDocument = editor.Bootstrap(
            "file:Aronchupa - Rave In The Grave HD Layout.xsq",
            4);
        Equal("Rave in the Grave", raveDocument.SequenceName, "Rave imported song title");
        Equal(4, raveDocument.Preview.Fixtures.Count, "Rave imported fixture count");
        True(raveDocument.CueSheet.Sections.Count > 300, "Rave imported cue richness");
        Equal("Imported XSQ controls", raveDocument.FixtureProfile.Name, "Rave imported profile");
        Equal(43, raveDocument.FixtureProfile.Pan.Park, "Rave imported pan center");
        var expandedRaveProfile = raveDocument.FixtureProfile with
        {
            Name = "Imported XSQ controls · edited",
            Tilt = raveDocument.FixtureProfile.Tilt with
            {
                Maximum = Math.Min(255, raveDocument.FixtureProfile.Tilt.Maximum + 1),
            },
        };
        var expandedRaveDocument = editor.Compile(new EditorRequest(
            raveDocument.SequenceId,
            raveDocument.HeadCount,
            raveDocument.CueSheet,
            expandedRaveProfile,
            raveDocument.SuggestedOutputFileName,
            TimingSourceName: raveDocument.Timing.DefaultBeatSource));
        Equal(expandedRaveProfile.Tilt.Maximum, expandedRaveDocument.FixtureProfile.Tilt.Maximum,
            "Rave edited tilt boundary preserved");
        Equal(expandedRaveProfile.Tilt.Maximum, expandedRaveDocument.Validation.Metrics.TiltMaximum,
            "Rave edited tilt boundary reported");
        True(raveDocument.Validation.Issues.Count == 0, "Rave imported validation");
        True(raveDocument.Validation.Metrics.PanDynamicPercent > 10, "Rave imported pan motion");
        True(raveDocument.Validation.Metrics.TiltDynamicPercent > 50, "Rave imported tilt motion");
        True(raveDocument.Validation.Metrics.DimmerVisiblePercent > 70, "Rave imported dimmer activity");
        True(raveDocument.Validation.Metrics.ShutterOpenPercent > 99, "Rave imported shutter coverage");
        var raveAtFifteenSeconds = raveDocument.Preview.Fixtures.Select(fixture => new
        {
            Pan = EvaluatePreview(fixture.Pan, 15000),
            Tilt = EvaluatePreview(fixture.Tilt, 15000),
            Dimmer = EvaluatePreview(fixture.Dimmer, 15000),
        }).ToArray();
        True(raveAtFifteenSeconds.Select(value => value.Pan).Distinct().Count() > 1,
            "Rave imported pan positions");
        True(raveAtFifteenSeconds.Select(value => value.Tilt).Distinct().Count() > 1,
            "Rave imported tilt positions");
        True(raveAtFifteenSeconds.All(value => value.Dimmer > 0), "Rave imported visible beams");
        var raveFirstEffectPan = raveDocument.Preview.Fixtures
            .Select(fixture => EvaluatePreview(fixture.Pan, 8200))
            .ToArray();
        True(raveFirstEffectPan.Take(2).All(value => value > raveDocument.FixtureProfile.Pan.Park),
            "Rave first pan bank points right");
        True(raveFirstEffectPan.Skip(2).All(value => value < raveDocument.FixtureProfile.Pan.Park),
            "Rave second pan bank points left");
        True(
            Math.Abs(
                (raveFirstEffectPan[0] - raveDocument.FixtureProfile.Pan.Park) -
                (raveDocument.FixtureProfile.Pan.Park - raveFirstEffectPan[^1])) <= 1,
            "Rave first effect pan symmetry");
        var raveResult = editor.Generate(new EditorRequest(
            "file:Aronchupa - Rave In The Grave HD Layout.xsq",
            4,
            raveDocument.CueSheet,
            raveDocument.FixtureProfile,
            "rave-web-test.xsq",
            Force: true,
            TimingSourceName: raveDocument.Timing.DefaultBeatSource));
        Equal(raveSourceBefore, File.ReadAllText(raveSourcePath), "Rave source remains unchanged");
        var raveOutput = new XmlDocument();
        raveOutput.Load(raveResult.OutputPath);
        True(raveOutput.SelectSingleNode("/xsequence/ElementEffects/Element[@name='MH Pan']") is not null,
            "Rave output preserves source pan controls");
        Equal(
            4,
            raveOutput.SelectNodes(
                "/xsequence/ElementEffects/Element[@name='MH-Pan']/Strand[@index='0']/Node")?.Count ?? 0,
            "Rave output adds editor pan controls");

        var firstSection = document.CueSheet.Sections[0] with
        {
            Pattern = "Reveal",
            Pan = null,
            Tilt = null,
            Dimmer = null,
            ShutterOpen = null,
            PanKeys = null,
            TiltKeys = null,
            DimmerKeys = null,
            Heads = [1, 3, 4],
        };
        var cueSheet = document.CueSheet with
        {
            Sections = [firstSection, .. document.CueSheet.Sections.Skip(1)],
        };
        var request = new EditorRequest(
            "liljon",
            4,
            cueSheet,
            document.FixtureProfile,
            "web-test.xsq",
            Force: true);
        var compiled = editor.Compile(request);
        Equal(18d, compiled.Validation.Metrics.PanDynamicPercent, "web editor live compile");

        var generated = editor.Generate(request);
        True(File.Exists(generated.OutputPath), "web editor generated output");
        var outputDocument = new XmlDocument();
        outputDocument.Load(generated.OutputPath);
        True(
            outputDocument.SelectSingleNode("/xsequence/ElementEffects/Element[@name='MH-Dimmers']") is not null,
            "web editor generated dimmer control");

        var boundedProfile = document.FixtureProfile with
        {
            Pan = new AxisProfileSettings(100, 150, 120, 100, 150, false),
            Tilt = new AxisProfileSettings(60, 110, 70, 80, 100, false),
        };
        var boundedCueSheet = document.CueSheet;
        var boundedCueSheetBefore = JsonSerializer.Serialize(boundedCueSheet);
        True(boundedCueSheet.Sections
                .SelectMany(section => (section.PanKeys ?? []).Concat(section.TiltKeys ?? []))
                .SelectMany(key => Regex.Matches(key, "\\d+").Select(match => int.Parse(match.Value)))
                .Any(value => value < 60 || value > 150),
            "bounded generation test includes out-of-range explicit curve values");
        var boundedGeneration = editor.Generate(new EditorRequest(
            "liljon",
            4,
            boundedCueSheet,
            boundedProfile,
            "bounded-web-test.xsq",
            Force: true,
            TimingSourceName: "Beats"));
        Equal(boundedCueSheetBefore, JsonSerializer.Serialize(boundedCueSheet),
            "bounded generation preserves explicit cue-sheet curves");
        var boundedOutput = new XmlDocument();
        boundedOutput.Load(boundedGeneration.OutputPath);
        var boundedPanValues = ReadGeneratedDmxValues(boundedOutput, "MH-Pan");
        var boundedTiltValues = ReadGeneratedDmxValues(boundedOutput, "MH-Tilt");
        True(boundedPanValues.Count > 0 && boundedPanValues.All(value => value is >= 99.9 and <= 150.1),
            "generated pan EffectDB values respect configured boundaries");
        True(boundedTiltValues.Count > 0 && boundedTiltValues.All(value => value is >= 59.9 and <= 110.1),
            "generated tilt EffectDB values respect configured boundaries");

        var stormDocument = editor.Bootstrap("storm", 4);
        var stormResult = editor.Generate(new EditorRequest(
            "storm",
            4,
            stormDocument.CueSheet,
            stormDocument.FixtureProfile,
            "storm-web-test.xsq",
            Force: true));
        var stormSource = new XmlDocument();
        stormSource.Load(stormSourcePath);
        var stormOutput = new XmlDocument();
        stormOutput.Load(stormResult.OutputPath);
        foreach (var fixtureName in new[] { "Mover1", "Mover2", "Mover3", "Mover4" })
        {
            Equal(
                stormSource.SelectSingleNode($"/xsequence/ElementEffects/Element[@name='{fixtureName}']")?.OuterXml,
                stormOutput.SelectSingleNode($"/xsequence/ElementEffects/Element[@name='{fixtureName}']")?.OuterXml,
                $"web editor preserves Storm {fixtureName}");
        }
        foreach (var headCount in new[] { 2, 3 })
        {
            var countDocument = editor.Bootstrap("storm", headCount);
            var countResult = editor.Generate(new EditorRequest(
                "storm",
                headCount,
                countDocument.CueSheet,
                countDocument.FixtureProfile,
                $"storm-{headCount}-head-test.xsq",
                Force: true));
            var countOutput = new XmlDocument();
            countOutput.Load(countResult.OutputPath);
            foreach (var fixtureNumber in Enumerable.Range(1, 4))
            {
                var fixture = countOutput.SelectSingleNode(
                    $"/xsequence/ElementEffects/Element[@name='Mover{fixtureNumber}']");
                True(
                    fixtureNumber <= headCount ? fixture is not null : fixture is null,
                    $"Storm {headCount}-head legacy fixture {fixtureNumber}");
            }
        }
    }

    private static double EvaluatePreview(IReadOnlyList<PreviewCurveSegment> segments, int timeMs)
    {
        var segment = segments.First(item => item.StartMs <= timeMs && item.EndMs > timeMs);
        return PreviewCompiler.Evaluate(segment, timeMs);
    }

    private static IReadOnlyList<double> ReadGeneratedDmxValues(XmlDocument document, string modelName)
    {
        var effectDb = document.SelectNodes("/xsequence/EffectDB/Effect")!
            .Cast<XmlElement>()
            .ToArray();
        var references = document.SelectNodes(
                $"/xsequence/ElementEffects/Element[@name='{modelName}']//Effect[@ref]")!
            .Cast<XmlElement>()
            .Select(effect => int.Parse(effect.GetAttribute("ref")))
            .Distinct()
            .ToArray();
        var values = new List<double>();
        foreach (var reference in references)
        {
            var definition = effectDb[reference].InnerText;
            values.AddRange(Regex.Matches(
                    definition,
                    @"(?:E_SLIDER_DMX1=|\|P\d+=)(\d+(?:\.\d+)?)",
                    RegexOptions.CultureInvariant)
                .Select(match => double.Parse(
                    match.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture)));
            var custom = Regex.Match(definition, @"Values=([^|]+)", RegexOptions.CultureInvariant);
            if (custom.Success)
            {
                values.AddRange(custom.Groups[1].Value.Split(';').Select(point =>
                    double.Parse(
                        point[(point.IndexOf(':') + 1)..],
                        System.Globalization.CultureInfo.InvariantCulture) * 255d));
            }
        }
        return values;
    }

        private static void VerifyBatchManifest()
        {
                using var temporary = new TemporaryDirectory();
                File.Copy(
                    FindRepositoryFile("A-Christmas-Storm(old_layout).xsq"),
                    Path.Combine(temporary.Path, "storm.xsq"));
                File.Copy(
                    FindRepositoryFile("All I Really Want For Christmas (feat.xsq"),
                    Path.Combine(temporary.Path, "liljon.xsq"));
                var manifestPath = Path.Combine(temporary.Path, "batch.json");
                File.WriteAllText(manifestPath, """
                        {
                            "sequences": [
                                { "sequencePath": "storm.xsq", "outputPath": "out/storm-custom.xsq", "headCount": 4 },
                                { "sequencePath": "liljon.xsq", "outputPath": "out/liljon-custom.xsq", "headCount": 4 }
                            ]
                        }
                        """);

                var manifest = BatchManifest.Load(manifestPath);
                Equal(Path.Combine(temporary.Path, "storm.xsq"), manifest.Sequences[0].SequencePath, "manifest relative input");
                Equal(Path.Combine(temporary.Path, "out", "storm-custom.xsq"), manifest.Sequences[0].OutputPath, "manifest relative output");

                var output = new StringWriter();
                var exitCode = new SequencerApplication(output).Run(CommandLineOptions.Parse(
                        ["--manifest", manifestPath, "--dry-run"]));
                Equal(0, exitCode, "manifest dry-run exit code");
                True(!Directory.Exists(Path.Combine(temporary.Path, "out")), "manifest dry-run does not create output directory");

                exitCode = new SequencerApplication(output).Run(CommandLineOptions.Parse(
                    ["--manifest", manifestPath, "--force"]));
                Equal(0, exitCode, "manifest generation exit code");
                True(File.Exists(Path.Combine(temporary.Path, "out", "storm-custom.xsq")), "manifest Storm output");
                True(File.Exists(Path.Combine(temporary.Path, "out", "liljon-custom.xsq")), "manifest Lil Jon output");

                Throws<CommandLineException>(
                        () => CommandLineOptions.Parse(["--manifest", manifestPath, "--target", "All"]),
                        "manifest generation mode exclusivity");

                var duplicatePath = Path.Combine(temporary.Path, "duplicate.json");
                File.WriteAllText(duplicatePath, """
                        {
                            "sequences": [
                                { "sequencePath": "storm.xsq", "outputPath": "same.xsq" },
                                { "sequencePath": "liljon.xsq", "outputPath": "same.xsq" }
                            ]
                        }
                        """);
                Throws<InvalidDataException>(() => BatchManifest.Load(duplicatePath), "duplicate manifest output");
        }

    private static void VerifyArchiveSelection()
    {
        using var temporary = new TemporaryDirectory();
        var priorityPath = Path.Combine(temporary.Path, "priority.xsqz");
        using (var archive = ZipFile.Open(priorityPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "xlights_rgbeffects.xml", "<xrgb marker='root'/>");
            AddEntry(archive, "nested/show/xlights_rgbeffects.xml", "<xrgb marker='nested'/>");
        }
        Equal("root", RgbEffectsReader.Read(priorityPath).Document.DocumentElement?.GetAttribute("marker"), "root-most archive entry");

        var ambiguousPath = Path.Combine(temporary.Path, "ambiguous.zip");
        using (var archive = ZipFile.Open(ambiguousPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "show-a/xlights_rgbeffects.xml", "<xrgb/>");
            AddEntry(archive, "show-b/xlights_rgbeffects.xml", "<xrgb/>");
        }
        Throws<InvalidDataException>(() => RgbEffectsReader.Read(ambiguousPath), "equal-depth archive entries");
    }

    private static void VerifyLibraryIntelligence()
    {
        using var temporary = new TemporaryDirectory();
        const string activeXml = """
            <xsequence>
              <head><sequenceDuration>1.000</sequenceDuration></head>
                            <EffectDB>
                                <Effect>E_VALUECURVE_DMX1=Active=TRUE|P1=0.00|P2=100.00|</Effect>
                                <Effect>E_TEXTCTRL_Eff_On_Start=5,E_TEXTCTRL_Eff_On_End=5</Effect>
                                <Effect>E_TEXTCTRL_Eff_On_Start=100,E_TEXTCTRL_Eff_On_End=100</Effect>
                            </EffectDB>
              <ElementEffects>
                <Element type="model" name="MH Pan"><Strand index="0"><Node index="0"><Effect ref="0" name="DMX" startTime="0" endTime="1000" /></Node></Strand></Element>
                                <Element type="model" name="MH Dimmer"><EffectLayer><Effect ref="1" name="On" startTime="0" endTime="1000" /></EffectLayer></Element>
                                <Element type="model" name="MH Shutter"><EffectLayer><Effect ref="2" name="On" startTime="0" endTime="1000" /></EffectLayer></Element>
              </ElementEffects>
            </xsequence>
            """;
        File.WriteAllText(Path.Combine(temporary.Path, "active-a.xsq"), activeXml);
        File.WriteAllText(Path.Combine(temporary.Path, "active-b.xsq"), activeXml);
        File.WriteAllText(Path.Combine(temporary.Path, "derived-4MH.xsq"), activeXml);
        File.WriteAllText(
            Path.Combine(temporary.Path, "placeholder.xsq"),
            "<xsequence><ElementEffects><Element type='model' name='MH Pan'><EffectLayer /></Element></ElementEffects></xsequence>");

        var results = SourceLibraryClassifier.Classify(temporary.Path);
        Equal(SourceClassification.ActiveReference,
            results.Single(result => result.FileName == "active-a.xsq").Classification,
            "active source classification");
        Equal(SourceClassification.Duplicate,
            results.Single(result => result.FileName == "active-b.xsq").Classification,
            "duplicate source classification");
        Equal(SourceClassification.GeneratedOutput,
            results.Single(result => result.FileName == "derived-4MH.xsq").Classification,
            "generated output classification");
        Equal(SourceClassification.InactivePlaceholder,
            results.Single(result => result.FileName == "placeholder.xsq").Classification,
            "placeholder classification");
        var classificationJson = new StringWriter();
        SourceLibraryClassifier.WriteReport(results, classificationJson, "json");
        True(
            classificationJson.ToString().Contains("\"Classification\": \"ActiveReference\"", StringComparison.Ordinal),
            "classification JSON enum names");

        var audit = ReferenceActivityAuditor.Audit(
            temporary.Path,
            new ReferenceSource(ReferenceTier.Target, "active-a.xsq"));
        Equal(100d, audit.DynamicMotionPercent, "per-head dynamic audit");
        Equal(100d, audit.PerHeadDynamicAveragePercent, "per-head dynamic average");
        Equal(0d, audit.AggregateDynamicPercent, "aggregate dynamic separation");
        Equal(100d, audit.NearDarkMotionPercent, "near-dark movement audit");
        Equal(100d, audit.ShutterOpenPercent, "shutter-open audit");

        var csv = new StringWriter();
        ReferenceActivityAuditor.WriteReport(
            temporary.Path, csv, "active-a.xsq", format: AuditReportFormat.Csv);
        True(csv.ToString().StartsWith("tier,sequence", StringComparison.Ordinal), "audit CSV output");
        var json = new StringWriter();
        ReferenceActivityAuditor.WriteReport(
            temporary.Path, json, "active-a.xsq", format: AuditReportFormat.Json);
        True(json.ToString().Contains("\"Tier\": \"Target\"", StringComparison.Ordinal), "audit JSON output");

        var suggestion = ChoreographyAdvisor.Suggest(["Final Chorus"], audit).Single();
        True(suggestion.Pattern.Contains("Mirrored fan", StringComparison.Ordinal), "chorus pattern suggestion");
        True(suggestion.Rationale.Contains("dense", StringComparison.Ordinal), "audit density suggestion");
        Equal(
            "Phrase ramp",
            ChoreographyAdvisor.Suggest(["they"], audit).Single().Pattern,
            "short suggestion keyword boundary");

        True(CommandLineOptions.Parse(["--classify-sources"]).ClassifySources, "classification CLI mode");
        True(CommandLineOptions.Parse(["--suggest-patterns", "--sequence-path", "song.xsq"]).SuggestPatterns,
            "suggestion CLI mode");
        Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--classify-sources", "--target", "All"]),
            "classification mode exclusivity");
    }

    private static void VerifyLayoutDiscovery()
    {
        var document = new XmlDocument();
        document.LoadXml("""
            <xrgb>
              <models>
                <model name="Fixture West" DisplayAs="DmxMovingHeadAdv" WorldPosX="20" Tags="roof;west" NodeNames="Color,Dimmer,Shutter">
                  <subModel name="Dimmer" line0="2" />
                  <subModel name="Shutter" line0="3" />
                  <PanMotor ChannelCoarse="10" />
                  <TiltMotor ChannelCoarse="12" />
                </model>
                <model name="Fixture East" DisplayAs="DmxMovingHeadAdv" WorldPosX="80" Tags="roof;east" NodeNames="Color,Dimmer,Shutter">
                  <subModel name="Dimmer" line0="2" />
                  <subModel name="Shutter" line0="3" />
                  <PanMotor ChannelCoarse="10" />
                  <TiltMotor ChannelCoarse="12" />
                </model>
                <model name="Horizontal Axis" DisplayAs="Single Line" Advanced="1" parm1="2" String1="@Fixture West:10" String2="@Fixture East:10" />
                <model name="Vertical Axis" DisplayAs="Single Line" Advanced="1" parm1="2" String1="@Fixture West:12" String2="@Fixture East:1" />
                <model name="Intensity" DisplayAs="Single Line" Advanced="1" parm1="2" String1="@Fixture West:2" String2="@Fixture East:2" />
                <model name="Gate" DisplayAs="Single Line" Advanced="1" parm1="2" String1="@Fixture West:3" String2="@Fixture East:3" />
              </models>
              <modelGroups>
                <modelGroup name="Rig" models="Fixture West,Fixture East" />
              </modelGroups>
            </xrgb>
            """);

        var layout = MovingHeadLayoutResolver.Resolve(new RgbEffectsDocument(document, "memory"), "Rig");
        EqualSequence(["Fixture West", "Fixture East"], layout.Fixtures.Select(fixture => fixture.Name), "fixture ordering");
        Equal("Rig", layout.PrimaryGroupName, "primary group");
        Equal(4, layout.Controls.Count, "control count");
        True(layout.Controls.Any(control => control.Name == "Horizontal Axis" && control.Role == ControlRole.Pan), "pan discovery");
        True(layout.Warnings.Any(warning => warning.Contains("channel 1", StringComparison.Ordinal)), "wiring warning");

        var eastOnly = MovingHeadLayoutSelector.Select(layout, new FixtureSelectionSettings
        {
            IncludeNames = ["Fixture East"],
        });
        EqualSequence(["Fixture East"], eastOnly.Fixtures.Select(fixture => fixture.Name), "exact fixture include");
        EqualSequence(
            [1],
            eastOnly.Controls.Single(control => control.Role == ControlRole.Pan).NodeIndexes!,
            "selected control node index");

        var excluded = MovingHeadLayoutSelector.Select(layout, new FixtureSelectionSettings
        {
            ExcludeNames = ["Fixture West"],
        });
        EqualSequence(["Fixture East"], excluded.Fixtures.Select(fixture => fixture.Name), "exact fixture exclude");

        var tagged = MovingHeadLayoutSelector.Select(layout, new FixtureSelectionSettings
        {
            IncludeTags = ["west"],
        });
        EqualSequence(["Fixture West"], tagged.Fixtures.Select(fixture => fixture.Name), "fixture tag include");

        var grouped = MovingHeadLayoutSelector.Select(layout, new FixtureSelectionSettings
        {
            IncludeGroups = ["Rig"],
        });
        Equal(2, grouped.Fixtures.Count, "fixture group include");
        Throws<InvalidDataException>(
            () => MovingHeadLayoutSelector.Select(layout, new FixtureSelectionSettings { IncludeNames = ["Missing"] }),
            "unknown fixture rejection");
    }

    private static void VerifySequenceSelection()
    {
        var allOptions = CommandLineOptions.Parse(["--target", "All"]);
        True(allOptions.TargetAll, "target All");
        Equal<string?>(null, allOptions.SequencePath, "All sequence path");

        var singleOptions = CommandLineOptions.Parse(["--sequence-path", "song.xsq"]);
        True(!singleOptions.TargetAll, "single target mode");
        Equal("song.xsq", singleOptions.SequencePath, "single sequence path");

        var auditOptions = CommandLineOptions.Parse(["--audit-references"]);
        True(auditOptions.AuditReferences, "reference audit mode");
        var auditPathOptions = CommandLineOptions.Parse(
            ["--audit-references", "--sequence-path", "generated.xsq"]);
        Equal("generated.xsq", auditPathOptions.SequencePath, "single-sequence audit path");

        var filteredOptions = CommandLineOptions.Parse(
            ["--target", "All", "--include-fixture", "West", "--exclude-fixture", "East", "--include-fixture-tag", "roof"]);
        EqualSequence(["West"], filteredOptions.IncludeFixtures!, "CLI fixture include");
        EqualSequence(["East"], filteredOptions.ExcludeFixtures!, "CLI fixture exclude");
        EqualSequence(["roof"], filteredOptions.IncludeFixtureTags!, "CLI fixture tag");

        Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--target", "Storm"]),
            "legacy shorthand rejection");
        Throws<CommandLineException>(
            () => CommandLineOptions.Parse([]),
            "missing generation selector");
        Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--target", "All", "--sequence-path", "song.xsq"]),
            "mutually exclusive selectors");
        Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--audit-references", "--target", "All"]),
            "audit target rejection");

        using var temporary = new TemporaryDirectory();
        var stormPath = Path.Combine(temporary.Path, "custom-storm.xsq");
        File.WriteAllText(stormPath, "<xsequence><head><song>A Christmas Storm</song></head></xsequence>");
        var stormPlan = SequenceDefinitionResolver.Create(stormPath, 6, layoutDriven: false);
        Equal(Path.GetFullPath(stormPath), stormPlan.SourceFileName, "resolved source path");
        Equal(
            Path.Combine(temporary.Path, "custom-storm-6MH.xsq"),
            stormPlan.OutputFileName,
            "source-derived output path");
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void EqualSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
        }
    }

    private static void True(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException($"{label}: expected true.");
        }
    }

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }

    private static string FindRepositoryFile(string fileName)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
        }
        throw new FileNotFoundException($"Repository fixture was not found: {fileName}");
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"moving-head-tests-{Guid.NewGuid():N}");

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}