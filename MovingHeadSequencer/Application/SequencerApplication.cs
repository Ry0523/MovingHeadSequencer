using System.Xml;
using MovingHeadSequencer.Audit;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Cli;
using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Generation;
using MovingHeadSequencer.Layout;
using MovingHeadSequencer.Library;
using MovingHeadSequencer.Sequences;
using MovingHeadSequencer.Suggestions;
using MovingHeadSequencer.Timing;
using MovingHeadSequencer.Validation;

namespace MovingHeadSequencer.Application;

internal sealed class SequencerApplication(TextWriter output)
{
    public int Run(CommandLineOptions options)
    {
        var configurationPath = ResolveConfigurationPath(options);
        var configuration = GenerationSettings.Load(configurationPath);
        var baseSettings = ResolveSettings(configuration, options, null);

        if (options.InspectLayout)
        {
            var layout = ResolveLayout(options.WorkspaceRoot, baseSettings);
            if (layout is null)
            {
                throw new InvalidOperationException("--inspect-layout requires --rgb-effects-path.");
            }

            LayoutReporter.Write(layout, output);
            return 0;
        }

        if (options.AuditReferences)
        {
            var auditSettings = ResolveSettings(configuration, options, options.SequencePath).Audit;
            var format = Enum.Parse<AuditReportFormat>(
                options.AuditFormat ?? auditSettings.OutputFormat,
                ignoreCase: true);
            if (string.IsNullOrWhiteSpace(options.AuditOutputPath))
            {
                ReferenceActivityAuditor.WriteReport(
                    options.WorkspaceRoot,
                    output,
                    options.SequencePath,
                    auditSettings,
                    format);
            }
            else
            {
                var auditPath = ResolveInputPath(options.WorkspaceRoot, options.AuditOutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(auditPath))!);
                using var writer = new StreamWriter(auditPath);
                ReferenceActivityAuditor.WriteReport(
                    options.WorkspaceRoot,
                    writer,
                    options.SequencePath,
                    auditSettings,
                    format);
                output.WriteLine($"Wrote audit report: {auditPath}");
            }
            return 0;
        }

        if (options.ClassifySources)
        {
            WriteClassification(options);
            return 0;
        }

        if (options.SuggestPatterns)
        {
            var sequencePath = ResolveInputPath(options.WorkspaceRoot, options.SequencePath!);
            var auditSettings = ResolveSettings(configuration, options, sequencePath).Audit;
            var audit = ReferenceActivityAuditor.Audit(
                options.WorkspaceRoot,
                new ReferenceSource(ReferenceTier.Target, sequencePath),
                auditSettings);
            var suggestions = ChoreographyAdvisor.Suggest(
                ChoreographyAdvisor.ReadTimingLabels(sequencePath),
                audit);
            ChoreographyAdvisor.WriteReport(sequencePath, audit, suggestions, output);
            return 0;
        }

        var generator = new SequenceGenerator(options.WorkspaceRoot, options.Force, output);
        var prepared = new List<PreparedGeneration>();
        var preparationErrors = new List<string>();
        if (options.TargetAll)
        {
            prepared.Add(PrepareConfigured(
                Path.Combine(options.WorkspaceRoot, "A-Christmas-Storm(old_layout).xsq"),
                configuration,
                options, null, null, useBundledOutputName: true));
            prepared.Add(PrepareConfigured(
                Path.Combine(options.WorkspaceRoot, "All I Really Want For Christmas (feat.xsq"),
                configuration,
                options, null, null, useBundledOutputName: true));
        }
        else if (!string.IsNullOrWhiteSpace(options.ManifestPath))
        {
            var manifest = BatchManifest.Load(ResolveInputPath(options.WorkspaceRoot, options.ManifestPath));
            foreach (var entry in manifest.Sequences)
            {
                try
                {
                    prepared.Add(PrepareConfigured(
                        entry.SequencePath,
                        configuration,
                        options,
                        entry.ToOverride(),
                        entry.OutputPath));
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException)
                {
                    preparationErrors.Add($"{entry.SequencePath}: {exception.Message}");
                }
            }
        }
        else
        {
            var sequencePath = ResolveInputPath(options.WorkspaceRoot, options.SequencePath!);
            prepared.Add(PrepareConfigured(sequencePath, configuration, options, null, null));
        }

        if (preparationErrors.Count > 0)
        {
            output.WriteLine("Batch preparation errors:");
            foreach (var error in preparationErrors)
            {
                output.WriteLine($"  {error}");
            }
            throw new InvalidDataException($"{preparationErrors.Count} batch item(s) could not be prepared.");
        }

        if (options.DryRun || prepared.Any(item => item.Report.Issues.Count > 0))
        {
            foreach (var item in prepared)
            {
                ValidationReporter.WriteSummary(item.Report, output);
            }
        }
        if (!string.IsNullOrWhiteSpace(options.ValidationReportPath))
        {
            ValidationReporter.WriteJson(
                ResolveInputPath(options.WorkspaceRoot, options.ValidationReportPath),
                prepared.Select(item => item.Report).ToArray());
        }
        if (prepared.Any(item => item.Report.HasErrors(item.Settings.Validation.TreatWarningsAsErrors)))
        {
            throw new InvalidDataException("Generation validation failed. Review the validation summary or JSON report.");
        }
        if (options.DryRun)
        {
            return 0;
        }
        foreach (var item in prepared)
        {
            generator.Generate(item.Plan, item.Layout, item.Settings.HeadCount);
        }
        return 0;
    }

    private static PreparedGeneration PrepareConfigured(
        string sequencePath,
        GenerationSettings configuration,
        CommandLineOptions options,
        SequenceOverrideSettings? invocationOverride,
        string? outputPath,
        bool useBundledOutputName = false)
    {
        var settings = ResolveSettings(configuration, options, sequencePath, invocationOverride);
        var layout = ResolveLayout(options.WorkspaceRoot, settings);
        var fixtureNames = layout is not null && layout.Fixtures.Count == settings.HeadCount
            ? layout.Fixtures.Select(fixture => fixture.Name).ToArray()
            : Enumerable.Range(1, settings.HeadCount).Select(index => $"Mover{index}").ToArray();
        settings = settings with
        {
            FixtureProfile = settings.FixtureProfile.ResolveFixtures(fixtureNames),
        };
        var plan = SequenceDefinitionResolver.Create(
            sequencePath,
            settings.HeadCount,
            layout is not null,
            settings.FixtureProfile);
        if (!string.IsNullOrWhiteSpace(settings.CueSheetPath))
        {
            var timing = XsqTimingMapReader.Read(sequencePath);
            var beatSource = timing.BeatSources.FirstOrDefault(source =>
                source.TrackName.Equals(timing.DefaultBeatSource, StringComparison.OrdinalIgnoreCase));
            plan = CueSheetComposer.Apply(
                plan,
                CueSheet.Load(ResolveInputPath(options.WorkspaceRoot, settings.CueSheetPath)),
                settings.HeadCount,
                settings.FixtureProfile,
                beatSource?.Beats.Select(beat => beat.TimeMs).ToArray());
        }
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            plan = plan with { OutputFileName = outputPath };
        }
        else if (useBundledOutputName)
        {
            var fileName = plan.Kind switch
            {
                SequenceKind.Storm => $"A-Christmas-Storm(old_layout)-{settings.HeadCount}MH.xsq",
                SequenceKind.LilJon => $"All-I-Really-Want-For-Christmas-{settings.HeadCount}MH.xsq",
                SequenceKind.Generic => $"{Path.GetFileNameWithoutExtension(sequencePath)}-{settings.HeadCount}MH.xsq",
                _ => throw new ArgumentOutOfRangeException(),
            };
            plan = plan with { OutputFileName = Path.Combine(options.WorkspaceRoot, fileName) };
        }
        return new PreparedGeneration(
            plan,
            layout,
            settings,
            SequencePlanValidator.Validate(plan, settings, layout));
    }

    private static ResolvedGenerationSettings ResolveSettings(
        GenerationSettings configuration,
        CommandLineOptions options,
        string? sequencePath,
        SequenceOverrideSettings? invocationOverride = null) =>
        configuration.Resolve(
            options.HeadCountSpecified ? options.HeadCount : null,
            options.AllowLayoutWarningsSpecified ? options.AllowLayoutWarnings : null,
            options.RgbEffectsPath,
            options.MovingHeadGroupName,
            GetCliFixtureSelection(options),
            options.CueSheetPath,
            invocationOverride,
            sequencePath);

    private static Domain.MovingHeadLayout? ResolveLayout(
        string workspaceRoot,
        ResolvedGenerationSettings settings) =>
        string.IsNullOrWhiteSpace(settings.RgbEffectsPath)
            ? null
            : MovingHeadLayoutSelector.Select(
                MovingHeadLayoutResolver.Resolve(
                    RgbEffectsReader.Read(ResolveInputPath(workspaceRoot, settings.RgbEffectsPath)),
                    settings.MovingHeadGroupName),
                settings.FixtureSelection);

    private static FixtureSelectionSettings? GetCliFixtureSelection(CommandLineOptions options)
    {
        if (options.IncludeFixtures is null && options.ExcludeFixtures is null &&
            options.IncludeFixtureGroups is null && options.IncludeFixtureTags is null)
        {
            return null;
        }
        return new FixtureSelectionSettings
        {
            IncludeNames = options.IncludeFixtures ?? [],
            ExcludeNames = options.ExcludeFixtures ?? [],
            IncludeGroups = options.IncludeFixtureGroups ?? [],
            IncludeTags = options.IncludeFixtureTags ?? [],
        };
    }

    private static string? ResolveConfigurationPath(CommandLineOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            return ResolveInputPath(options.WorkspaceRoot, options.ConfigPath);
        }
        var projectConfiguration = Path.Combine(options.WorkspaceRoot, "moving-head-sequencer.json");
        return File.Exists(projectConfiguration) ? projectConfiguration : null;
    }

    private void WriteClassification(CommandLineOptions options)
    {
        var results = SourceLibraryClassifier.Classify(options.WorkspaceRoot);
        if (string.IsNullOrWhiteSpace(options.ClassificationOutputPath))
        {
            SourceLibraryClassifier.WriteReport(results, output, options.ClassificationFormat);
            return;
        }
        var path = ResolveInputPath(options.WorkspaceRoot, options.ClassificationOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path);
        SourceLibraryClassifier.WriteReport(results, writer, options.ClassificationFormat);
        output.WriteLine($"Wrote source classification: {path}");
    }

    private sealed record PreparedGeneration(
        SequencePlan Plan,
        Domain.MovingHeadLayout? Layout,
        ResolvedGenerationSettings Settings,
        SequenceValidationReport Report);

    private static string ResolveInputPath(string workspaceRoot, string path) =>
        Path.IsPathFullyQualified(path) ? path : Path.Combine(workspaceRoot, path);
}