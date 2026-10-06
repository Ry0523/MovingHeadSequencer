using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Generation;
using MovingHeadSequencer.Preview;
using MovingHeadSequencer.Sequences;
using MovingHeadSequencer.Timing;
using MovingHeadSequencer.Validation;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;

namespace MovingHeadSequencer.Web;

internal sealed class EditorService : IEditorService
{
    private const string StormFileName = "A-Christmas-Storm(old_layout).xsq";
    private const string LilJonFileName = "All I Really Want For Christmas (feat.xsq";

    private readonly string workspaceRoot;
    private readonly SequenceOption[] sequenceOptions;
    private readonly SequenceBackupStore backupStore;
    private readonly SequenceProjectStore projectStore;

    public string WorkspaceRoot => workspaceRoot;

    public EditorService(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        sequenceOptions = DiscoverSequenceOptions(this.workspaceRoot);
        backupStore = new SequenceBackupStore(this.workspaceRoot);
        projectStore = new SequenceProjectStore(this.workspaceRoot);
    }

    public EditorDocument Bootstrap(string? sequenceId, int headCount)
    {
        var sequence = ResolveSequence(sequenceId);
        var context = Build(sequence, headCount, new FixtureProfileSettings(), cueSheet: null, timingSourceName: null);
        return CreateDocument(context);
    }

    public EditorDocument Compile(EditorRequest request)
    {
        var sequence = ResolveSequence(request.SequenceId);
        EnsureExpectedSourceUnchanged(sequence, request.ExpectedSourceFingerprint);
        var context = Build(
            sequence,
            request.HeadCount,
            request.FixtureProfile ?? new FixtureProfileSettings(),
            request.CueSheet,
            request.TimingSourceName);
        return CreateDocument(context);
    }

    public CueRegenerationResult Suggest(CueRegenerationRequest request)
    {
        var editor = request.Editor;
        var sequence = ResolveSequence(editor.SequenceId);
        EnsureExpectedSourceUnchanged(sequence, editor.ExpectedSourceFingerprint);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        editor.CueSheet.Validate("regeneration request");
        var timing = XsqTimingMapReader.Read(sourcePath);
        return CueRegenerationAdvisor.Suggest(
            sourcePath,
            editor.CueSheet,
            request.CueIndex,
            timing,
            editor.TimingSourceName);
    }

    public WholeSongRegenerationResult Regenerate(WholeSongRegenerationRequest request)
    {
        var editor = request.Editor;
        var sequence = ResolveSequence(editor.SequenceId);
        EnsureExpectedSourceUnchanged(sequence, editor.ExpectedSourceFingerprint);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        editor.CueSheet.Validate("whole-song regeneration request");
        var timing = XsqTimingMapReader.Read(sourcePath);
        var advice = CueRegenerationAdvisor.Regenerate(
            sourcePath,
            editor.CueSheet,
            timing,
            editor.TimingSourceName);
        var context = Build(
            sequence,
            editor.HeadCount,
            editor.FixtureProfile ?? new FixtureProfileSettings(),
            advice.CueSheet,
            editor.TimingSourceName);
        if (context.Report.HasErrors(treatWarningsAsErrors: false))
        {
            var errors = string.Join(
                "; ",
                context.Report.Issues
                    .Where(issue => issue.Severity == ValidationSeverity.Error)
                    .DistinctBy(issue => new { issue.Code, issue.Message, issue.Start, issue.End })
                    .Select(issue => issue.Start is null
                        ? $"{issue.Code}: {issue.Message}"
                        : FormatRegenerationIssue(issue, advice.CueSheet)));
            throw new InvalidDataException(
                $"Whole-song regeneration produced validation errors and was not applied. {errors}");
        }
        var averageConfidence = advice.AppliedChoices.Count == 0
            ? 0
            : Math.Round(advice.AppliedChoices.Average(choice => choice.Confidence), 1);
        return new WholeSongRegenerationResult(
            CreateDocument(context),
            advice.AppliedChoices,
            averageConfidence);
    }

    private static string FormatRegenerationIssue(ValidationIssue issue, CueSheet cueSheet)
    {
        var section = cueSheet.Sections.FirstOrDefault(candidate =>
            candidate.StartMs <= issue.Start && candidate.EndMs >= issue.End);
        if (section is null)
        {
            return $"{issue.Code} at {issue.Start}-{issue.End}: {issue.Message}";
        }
        var pattern = cueSheet.Resolve(section);
        return $"{issue.Code} at {issue.Start}-{issue.End}: {issue.Message} " +
            $"Cue '{section.Name}' uses pan={pattern.Pan}, tilt={pattern.Tilt}, rhythm={pattern.Rhythm}.";
    }

    public GenerationResult Generate(EditorRequest request)
    {
        var sequence = ResolveSequence(request.SequenceId);
        EnsureExpectedSourceUnchanged(sequence, request.ExpectedSourceFingerprint);
        var context = Build(
            sequence,
            request.HeadCount,
            request.FixtureProfile ?? new FixtureProfileSettings(),
            request.CueSheet,
            request.TimingSourceName);
        if (context.Report.HasErrors(treatWarningsAsErrors: false))
        {
            throw new InvalidDataException("The cue sheet has validation errors and cannot be generated.");
        }

        var outputFileName = string.IsNullOrWhiteSpace(request.OutputFileName)
            ? $"{sequence.OutputBaseName}-editor-{request.HeadCount}MH.xsq"
            : Path.GetFileName(request.OutputFileName);
        if (!Path.GetExtension(outputFileName).Equals(".xsq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Output filename must end in .xsq.");
        }

        var outputPath = Path.Combine(workspaceRoot, outputFileName);
        var messages = new StringWriter();
        var plan = context.Plan with { OutputFileName = outputPath };
        new SequenceGenerator(workspaceRoot, request.Force, messages).Generate(
            plan,
            layout: null,
            request.HeadCount);
        var report = context.Report with { Output = outputPath };
        return new GenerationResult(
            outputPath,
            report,
            messages.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    public SequenceBackupCatalog ListBackups(string sequenceId)
    {
        var sequence = ResolveSequence(sequenceId);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        return new SequenceBackupCatalog(sequence.Id, sequence.FileName, backupStore.List(sourcePath));
    }

    public OriginalGenerationResult GenerateOriginal(EditorRequest request)
    {
        var sequence = ResolveSequence(request.SequenceId);
        EnsureExpectedSourceUnchanged(sequence, request.ExpectedSourceFingerprint);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        var sourceFingerprint = backupStore.Fingerprint(sourcePath);
        var context = Build(
            sequence,
            request.HeadCount,
            request.FixtureProfile ?? new FixtureProfileSettings(),
            request.CueSheet,
            request.TimingSourceName);
        if (context.Report.HasErrors(treatWarningsAsErrors: false))
        {
            throw new InvalidDataException("The cue sheet has validation errors and cannot replace the original.");
        }
        if (!backupStore.Fingerprint(sourcePath).Equals(sourceFingerprint, StringComparison.Ordinal))
        {
            throw new IOException(
                "The original sequence changed while validation was in progress; no replacement was made.");
        }

        var stagingPath = backupStore.CreateStagingPath();
        var messages = new StringWriter();
        try
        {
            var plan = context.Plan with { OutputFileName = stagingPath };
            new SequenceGenerator(workspaceRoot, force: true, messages).Generate(
                plan,
                layout: null,
                request.HeadCount);
            var backup = backupStore.ReplaceOriginal(sourcePath, stagingPath, sourceFingerprint);
            projectStore.ArchiveAll(sourcePath);
            var replacementFingerprint = backupStore.Fingerprint(sourcePath);
            messages.WriteLine($"Backed up the original as {backup.Id}.");
            var report = context.Report with { Output = sourcePath };
            return new OriginalGenerationResult(
                sourcePath,
                replacementFingerprint,
                backup,
                report,
                messages.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }

    public RestoreBackupResult RestoreBackup(RestoreBackupRequest request)
    {
        var sequence = ResolveSequence(request.SequenceId);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        var result = backupStore.Restore(sourcePath, request.BackupId);
        projectStore.ArchiveAll(sourcePath);
        return result;
    }

    public StudioProjectLoadResult LoadProject(string sequenceId, int headCount)
    {
        var sequence = ResolveSequence(sequenceId);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        try
        {
            return new StudioProjectLoadResult(projectStore.Load(sourcePath, headCount));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            projectStore.Archive(sourcePath, headCount);
            return new StudioProjectLoadResult(
                null,
                $"The hidden project was invalid and was archived: {exception.Message}");
        }
    }

    public SaveStudioProjectResult SaveProject(SaveStudioProjectRequest request)
    {
        var editor = request.Editor;
        var sequence = ResolveSequence(editor.SequenceId);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        var fingerprint = backupStore.Fingerprint(sourcePath);
        if (!fingerprint.Equals(request.ExpectedSourceFingerprint, StringComparison.Ordinal))
        {
            throw new IOException(
                "The source sequence changed outside the editor; reload it before saving project changes.");
        }
        var context = Build(
            sequence,
            editor.HeadCount,
            editor.FixtureProfile ?? new FixtureProfileSettings(),
            editor.CueSheet,
            editor.TimingSourceName);
        if (context.Report.HasErrors(treatWarningsAsErrors: false))
        {
            throw new InvalidDataException("The editor has validation errors and cannot be auto-saved.");
        }
        var timestamp = DateTimeOffset.UtcNow;
        var revision = Guid.NewGuid().ToString("N");
        if (!backupStore.Fingerprint(sourcePath).Equals(fingerprint, StringComparison.Ordinal))
        {
            throw new IOException(
                "The source sequence changed while the project was being validated; no project was saved.");
        }
        var project = new StudioProject(
            SequenceProjectStore.ProjectFormat,
            SequenceProjectStore.ProjectVersion,
            revision,
            timestamp,
            new StudioProjectSource(
                sequence.Id,
                sequence.FileName,
                sequence.Name,
                fingerprint,
                context.CueSheet.DurationMs),
            new StudioProjectEditor(
                editor.HeadCount,
                editor.TimingSourceName,
                editor.OutputFileName,
                context.Settings.FixtureProfile,
                context.CueSheet),
            request.Preview,
            request.View,
            request.Regeneration);
        projectStore.Save(sourcePath, project, request.ExpectedProjectRevision);
        return new SaveStudioProjectResult(timestamp, fingerprint, revision);
    }

    public void ResetProject(ResetStudioProjectRequest request)
    {
        var sequence = ResolveSequence(request.SequenceId);
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        projectStore.Archive(sourcePath, request.HeadCount);
    }

    private void EnsureExpectedSourceUnchanged(
        SequenceOption sequence,
        string? expectedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            return;
        }
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        if (!backupStore.Fingerprint(sourcePath).Equals(expectedFingerprint, StringComparison.Ordinal))
        {
            throw new IOException(
                "The source sequence changed outside the editor; reload it before continuing.");
        }
    }

    private EditorDocument CreateDocument(EditorContext context) => new(
        sequenceOptions,
        workspaceRoot,
        context.Sequence.Id,
        context.Sequence.Name,
        context.Sequence.FileName,
        backupStore.Fingerprint(Path.Combine(workspaceRoot, context.Sequence.FileName)),
        context.Settings.HeadCount,
        $"{context.Sequence.OutputBaseName}-editor-{context.Settings.HeadCount}MH.xsq",
        context.Settings.FixtureProfile,
        context.CueSheet,
        context.Preview,
        context.Timing,
        context.Media,
        context.Report,
        Enum.GetNames<PanPattern>(),
        Enum.GetNames<TiltPattern>(),
        Enum.GetNames<DimmerPattern>(),
        Enum.GetNames<RhythmPattern>(),
        Enum.GetNames<MotionEnergy>(),
        Enum.GetNames<MotionShape>(),
        Enum.GetNames<MotionPhase>(),
        Enum.GetNames<IntensityEnvelope>());

    private EditorContext Build(
        SequenceOption sequence,
        int headCount,
        FixtureProfileSettings profile,
        CueSheet? cueSheet,
        string? timingSourceName)
    {
        if (!HeadCountPolicy.IsSupported(headCount))
        {
            throw new InvalidDataException($"Head count must be {HeadCountPolicy.RangeText}.");
        }
        var sourcePath = Path.Combine(workspaceRoot, sequence.FileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Sequence source was not found: {sourcePath}", sourcePath);
        }

        var fixtureNames = Enumerable.Range(1, headCount).Select(index => $"Mover{index}").ToArray();
        profile.Validate("editor request");
        profile = profile.ResolveFixtures(fixtureNames);
        var basePlan = SequenceDefinitionResolver.Create(sourcePath, headCount, layoutDriven: false, profile);
        if (basePlan.FixtureProfileOverride is not null && IsDefaultFixtureProfile(profile))
        {
            profile = basePlan.FixtureProfileOverride.ResolveFixtures(fixtureNames);
        }
        var timing = XsqTimingMapReader.Read(sourcePath);
        var beatSourceName = string.IsNullOrWhiteSpace(timingSourceName)
            ? timing.DefaultBeatSource
            : timingSourceName;
        var beatSource = timing.BeatSources.FirstOrDefault(source =>
            source.TrackName.Equals(beatSourceName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(timingSourceName) && beatSource is null)
        {
            throw new InvalidDataException($"Unknown beat timing source '{timingSourceName}'.");
        }
        cueSheet ??= CreateEditableCueSheet(basePlan, headCount, profile, sequence.Name);
        cueSheet.Validate("editor request");
        var plan = CueSheetComposer.Apply(
            basePlan,
            cueSheet,
            headCount,
            profile,
            beatSource?.Beats.Select(beat => beat.TimeMs).ToArray());
        var settings = new GenerationSettings
        {
            HeadCount = headCount,
            FixtureProfile = profile,
        }.Resolve(null, null, null, null, null, null, null, sourcePath) with
        {
            FixtureProfile = profile,
        };
        var report = SequencePlanValidator.Validate(plan, settings, layout: null);
        var preview = PreviewCompiler.Compile(plan, headCount, profile, fixtureNames);
        return new EditorContext(
            sequence,
            settings,
            cueSheet,
            plan,
            preview,
            timing,
            ReadMediaInfo(sourcePath),
            report);
    }

    private static bool IsDefaultFixtureProfile(FixtureProfileSettings profile)
    {
        var defaults = new FixtureProfileSettings();
        return profile.Name == defaults.Name &&
            profile.PanTravelDegrees == defaults.PanTravelDegrees &&
            profile.TiltTravelDegrees == defaults.TiltTravelDegrees &&
            profile.Pan == defaults.Pan &&
            profile.Tilt == defaults.Tilt &&
            profile.Fixtures.Count == 0;
    }

    private static CueSheet CreateEditableCueSheet(
        SequencePlan plan,
        int headCount,
        FixtureProfileSettings profile,
        string sequenceName)
    {
        var boundaries = plan.PanCues.SelectMany(cue => new[] { cue.Start, cue.End })
            .Concat(plan.TiltCues.SelectMany(cue => new[] { cue.Start, cue.End }))
            .Concat(plan.DimmerCues.SelectMany(cue => new[] { cue.Start, cue.End }))
            .Concat(plan.ShutterEvents.SelectMany(effect => new[] { effect.Start, effect.End }))
            .Append(0)
            .Append(plan.Duration)
            .Distinct()
            .Order()
            .ToArray();
        var sections = new List<CueSheetSection>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            if (end <= start)
            {
                continue;
            }
            var section = new CueSheetSection
            {
                Name = $"Cue {sections.Count + 1:00}",
                StartMs = start,
                EndMs = end,
                Pan = FindPattern(
                    plan.PanCues,
                    start,
                    Enum.GetValues<PanPattern>(),
                    pattern => PatternFactory.Pan(pattern, headCount, profile),
                    fallback: PanPattern.Park),
                Tilt = FindPattern(
                    plan.TiltCues,
                    start,
                    Enum.GetValues<TiltPattern>(),
                    pattern => PatternFactory.Tilt(pattern, headCount, profile),
                    fallback: TiltPattern.Park),
                Dimmer = FindPattern(
                    plan.DimmerCues,
                    start,
                    Enum.GetValues<DimmerPattern>(),
                    pattern => PatternFactory.Dimmer(pattern, headCount),
                    fallback: DimmerPattern.All0),
                PanKeys = CueCurveSlicer.SliceTrack(
                    plan.PanCues,
                    start,
                    end,
                    PatternFactory.Pan(PanPattern.Park, headCount, profile)),
                TiltKeys = CueCurveSlicer.SliceTrack(
                    plan.TiltCues,
                    start,
                    end,
                    PatternFactory.Tilt(TiltPattern.Park, headCount, profile)),
                DimmerKeys = CueCurveSlicer.SliceTrack(
                    plan.DimmerCues,
                    start,
                    end,
                    PatternFactory.Dimmer(DimmerPattern.All0, headCount)),
                ShutterOpen = plan.ShutterEvents.Any(effect => effect.Start <= start && effect.End >= end),
            };
            if (sections.Count > 0 && SameLook(sections[^1], section))
            {
                sections[^1] = sections[^1] with { EndMs = end };
            }
            else
            {
                sections.Add(section);
            }
        }

        return new CueSheet
        {
            Name = $"{sequenceName} editor cues",
            DurationMs = plan.Duration,
            Defaults = new CuePatternDefinition
            {
                Pan = PanPattern.Park,
                Tilt = TiltPattern.Park,
                Dimmer = DimmerPattern.All0,
                Rhythm = RhythmPattern.Off,
                MotionEnergy = MotionEnergy.Full,
                MotionShape = MotionShape.Smooth,
                MotionPhase = MotionPhase.Together,
                IntensityEnvelope = IntensityEnvelope.Steady,
                ShutterOpen = false,
            },
            Patterns = CreatePatternLibrary(),
            Sections = sections,
        };
    }

    private static TPattern FindPattern<TPattern>(
        IReadOnlyList<Cue> cues,
        int time,
        IEnumerable<TPattern> patterns,
        Func<TPattern, IReadOnlyList<string>> create,
        TPattern? fallback = null)
        where TPattern : struct, Enum
    {
        var cue = cues.FirstOrDefault(candidate => candidate.Start <= time && candidate.End > time);
        if (cue is null)
        {
            return fallback ?? throw new InvalidDataException(
                $"No {typeof(TPattern).Name} cue covers {time} ms.");
        }
        foreach (var pattern in patterns)
        {
            if (create(pattern).SequenceEqual(cue.Keys))
            {
                return pattern;
            }
        }
        return fallback ?? throw new InvalidDataException(
            $"Cue {cue.Start}-{cue.End} cannot be represented by a {typeof(TPattern).Name} editor pattern.");
    }

    private static bool SameLook(CueSheetSection left, CueSheetSection right) =>
        left.Pan == right.Pan && left.Tilt == right.Tilt && left.Dimmer == right.Dimmer &&
        left.Rhythm == right.Rhythm && left.ShutterOpen == right.ShutterOpen &&
        left.Heads.SequenceEqual(right.Heads) &&
        KeysEqual(left.PanKeys, right.PanKeys) &&
        KeysEqual(left.TiltKeys, right.TiltKeys) &&
        KeysEqual(left.DimmerKeys, right.DimmerKeys);

    private static bool KeysEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static Dictionary<string, CuePatternDefinition> CreatePatternLibrary() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Blackout"] = new()
            {
                Pan = PanPattern.Park,
                Tilt = TiltPattern.Park,
                Dimmer = DimmerPattern.All0,
                ShutterOpen = false,
            },
            ["Low Hold"] = new()
            {
                Pan = PanPattern.Fan,
                Tilt = TiltPattern.High,
                Dimmer = DimmerPattern.All45,
                ShutterOpen = true,
            },
            ["Reveal"] = new()
            {
                Pan = PanPattern.OpenFan,
                Tilt = TiltPattern.Rise,
                Dimmer = DimmerPattern.All100,
                ShutterOpen = true,
            },
            ["Sweep"] = new()
            {
                Pan = PanPattern.Cross,
                Tilt = TiltPattern.InverseBounce,
                Dimmer = DimmerPattern.All75,
                ShutterOpen = true,
            },
            ["Bounce"] = new()
            {
                Pan = PanPattern.Bounce,
                Tilt = TiltPattern.Bounce,
                Dimmer = DimmerPattern.All70,
                ShutterOpen = true,
            },
        };

    private SequenceOption ResolveSequence(string? sequenceId)
    {
        if (sequenceOptions.Length == 0)
        {
            throw new InvalidDataException(
                $"No valid XSQ sequences were found in '{workspaceRoot}'. Choose a workspace folder that contains xLights .xsq sequence files.");
        }

        var id = string.IsNullOrWhiteSpace(sequenceId)
            ? sequenceOptions.FirstOrDefault(sequence => sequence.Id == "liljon")?.Id ?? sequenceOptions[0].Id
            : sequenceId;
        return sequenceOptions.SingleOrDefault(sequence => sequence.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Unsupported editor sequence '{sequenceId}'.");
    }

    private static SequenceOption[] DiscoverSequenceOptions(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var discovered = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".xsq", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsGeneratedOutput(path))
            .Select(ReadSequenceOption)
            .Where(option => option is not null)
            .Select(option => option!)
            .ToArray();
        var duplicateNames = discovered
            .GroupBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return discovered
            .Select(option => duplicateNames.Contains(option.Name)
                ? option with { Name = $"{option.Name} - {option.FileName}" }
                : option)
            .OrderBy(option => option.Id == "storm" ? 0 : option.Id == "liljon" ? 1 : 2)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SequenceOption? ReadSequenceOption(string path)
    {
        try
        {
            string? song = null;
            double duration = 0;
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                if (reader.Name == "song")
                {
                    song = WebUtility.HtmlDecode(reader.ReadElementContentAsString().Trim());
                }
                else if (reader.Name == "sequenceDuration")
                {
                    double.TryParse(
                        reader.ReadElementContentAsString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out duration);
                }
                if (duration > 0 && !string.IsNullOrWhiteSpace(song))
                {
                    break;
                }
            }
            if (duration <= 0)
            {
                return null;
            }

            var fileName = Path.GetFileName(path);
            var baseName = Path.GetFileNameWithoutExtension(path);
            var id = fileName.Equals(StormFileName, StringComparison.OrdinalIgnoreCase)
                ? "storm"
                : fileName.Equals(LilJonFileName, StringComparison.OrdinalIgnoreCase)
                    ? "liljon"
                    : $"file:{fileName}";
            var outputBaseName = id switch
            {
                "storm" => "A-Christmas-Storm(old_layout)",
                "liljon" => "All-I-Really-Want-For-Christmas",
                _ => baseName,
            };
            return new SequenceOption(
                id,
                string.IsNullOrWhiteSpace(song) ? baseName : song,
                fileName,
                outputBaseName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    private static bool IsGeneratedOutput(string path) => Regex.IsMatch(
        Path.GetFileNameWithoutExtension(path),
        "(?:-editor)?-(?:[2-9]|1[0-2])MH$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static SequenceMediaInfo ReadMediaInfo(string sourcePath)
    {
        var document = new XmlDocument();
        document.Load(sourcePath);
        var declaredPath = document.SelectSingleNode("/xsequence/head/mediaFile")?.InnerText.Trim();
        var exists = !string.IsNullOrWhiteSpace(declaredPath) && File.Exists(declaredPath);
        return new SequenceMediaInfo(
            string.IsNullOrWhiteSpace(declaredPath) ? null : declaredPath,
            string.IsNullOrWhiteSpace(declaredPath) ? null : Path.GetFileName(declaredPath),
            exists,
            !exists);
    }

    private sealed record EditorContext(
        SequenceOption Sequence,
        ResolvedGenerationSettings Settings,
        CueSheet CueSheet,
        SequencePlan Plan,
        CompiledPreview Preview,
        TimingMap Timing,
        SequenceMediaInfo Media,
        SequenceValidationReport Report);
}
