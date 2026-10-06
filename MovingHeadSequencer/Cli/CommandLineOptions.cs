using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Cli;

internal sealed record CommandLineOptions(
    bool TargetAll,
    string? SequencePath,
    int HeadCount,
    string WorkspaceRoot,
    string? RgbEffectsPath,
    string? MovingHeadGroupName,
    bool InspectLayout,
    bool AuditReferences,
    bool AllowLayoutWarnings,
    bool Force,
    bool ShowHelp,
    string? ConfigPath = null,
    bool HeadCountSpecified = false,
    bool AllowLayoutWarningsSpecified = false,
    bool DryRun = false,
    string? ValidationReportPath = null,
    IReadOnlyList<string>? IncludeFixtures = null,
    IReadOnlyList<string>? ExcludeFixtures = null,
    IReadOnlyList<string>? IncludeFixtureGroups = null,
    IReadOnlyList<string>? IncludeFixtureTags = null,
    string? AuditFormat = null,
    string? AuditOutputPath = null,
    string? CueSheetPath = null,
    string? ManifestPath = null,
    bool ClassifySources = false,
    string ClassificationFormat = "table",
    string? ClassificationOutputPath = null,
    bool SuggestPatterns = false)
{
    public const string HelpText = """
        MovingHeadSequencer

        Usage:
          dotnet run --project tools/MovingHeadSequencer -- [options]

        Options:
                    --target All                      Generate all bundled sequences
                    --sequence-path <file.xsq>        Generate one supported sequence
                    --manifest <path>                 Generate a batch from a JSON manifest
          --head-count <2-12>               Moving-head count (default: 4)
          --workspace-root <path>           Sequence directory (default: current directory)
          --config <path>                   JSON configuration file
          --cue-sheet <path>                JSON cue sheet for the selected sequence
          --rgb-effects-path <path>         xlights_rgbeffects.xml, ZIP, or XSQZ package
          --moving-head-group-name <name>   Select one moving-head model group
          --include-fixture <name>          Include an exact fixture name (repeatable)
          --exclude-fixture <name>          Exclude an exact fixture name (repeatable)
          --include-fixture-group <name>    Include fixtures from a layout group
          --include-fixture-tag <tag>       Include fixtures carrying a tag
          --inspect-layout                  Print discovered layout without generating
          --audit-references                Audit references, or one --sequence-path
          --audit-format <table|csv|json>   Select audit output format
          --audit-output <path>             Write audit output to a file
          --classify-sources                Classify the workspace XSQ source library
          --classification-format <format>  table, csv, or json
          --classification-output <path>    Write source classification to a file
          --suggest-patterns                Suggest choreography for --sequence-path
          --allow-layout-warnings           Generate despite layout wiring warnings
          --dry-run                         Validate and summarize without writing XSQ files
          --validation-report <path>        Write machine-readable validation JSON
          --force                           Replace existing output files
          --help                            Show this help
        """;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var targetAll = false;
        string? sequencePath = null;
        var headCount = 4;
        var workspaceRoot = Directory.GetCurrentDirectory();
        string? rgbEffectsPath = null;
        string? movingHeadGroupName = null;
        string? configPath = null;
        var headCountSpecified = false;
        var inspectLayout = false;
        var auditReferences = false;
        var allowLayoutWarnings = false;
        var force = false;
        var showHelp = false;
        var dryRun = false;
        string? validationReportPath = null;
        var includeFixtures = new List<string>();
        var excludeFixtures = new List<string>();
        var includeFixtureGroups = new List<string>();
        var includeFixtureTags = new List<string>();
        string? auditFormat = null;
        string? auditOutputPath = null;
        string? cueSheetPath = null;
        string? manifestPath = null;
        var classifySources = false;
        var classificationFormat = "table";
        string? classificationOutputPath = null;
        var suggestPatterns = false;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--target":
                    var target = ReadValue(args, ref index, "--target");
                    if (!target.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CommandLineException(
                            $"Unsupported target '{target}'. Use --target All or --sequence-path <file.xsq>.");
                    }
                    targetAll = true;
                    break;
                case "--sequence-path":
                    sequencePath = ReadValue(args, ref index, "--sequence-path");
                    break;
                case "--manifest":
                    manifestPath = ReadValue(args, ref index, "--manifest");
                    break;
                case "--head-count":
                    headCount = ParseHeadCount(ReadValue(args, ref index, "--head-count"));
                    headCountSpecified = true;
                    break;
                case "--config":
                    configPath = ReadValue(args, ref index, "--config");
                    break;
                case "--cue-sheet":
                    cueSheetPath = ReadValue(args, ref index, "--cue-sheet");
                    break;
                case "--workspace-root":
                    workspaceRoot = ReadValue(args, ref index, "--workspace-root");
                    break;
                case "--rgb-effects-path":
                    rgbEffectsPath = ReadValue(args, ref index, "--rgb-effects-path");
                    break;
                case "--moving-head-group-name":
                    movingHeadGroupName = ReadValue(args, ref index, "--moving-head-group-name");
                    break;
                case "--include-fixture":
                    includeFixtures.Add(ReadValue(args, ref index, "--include-fixture"));
                    break;
                case "--exclude-fixture":
                    excludeFixtures.Add(ReadValue(args, ref index, "--exclude-fixture"));
                    break;
                case "--include-fixture-group":
                    includeFixtureGroups.Add(ReadValue(args, ref index, "--include-fixture-group"));
                    break;
                case "--include-fixture-tag":
                    includeFixtureTags.Add(ReadValue(args, ref index, "--include-fixture-tag"));
                    break;
                case "--inspect-layout":
                    inspectLayout = true;
                    break;
                case "--audit-references":
                    auditReferences = true;
                    break;
                case "--audit-format":
                    auditFormat = ReadValue(args, ref index, "--audit-format").ToLowerInvariant();
                    if (auditFormat is not ("table" or "csv" or "json"))
                    {
                        throw new CommandLineException("--audit-format must be table, csv, or json.");
                    }
                    break;
                case "--audit-output":
                    auditOutputPath = ReadValue(args, ref index, "--audit-output");
                    break;
                case "--classify-sources":
                    classifySources = true;
                    break;
                case "--classification-format":
                    classificationFormat = ReadValue(args, ref index, "--classification-format").ToLowerInvariant();
                    if (classificationFormat is not ("table" or "csv" or "json"))
                    {
                        throw new CommandLineException("--classification-format must be table, csv, or json.");
                    }
                    break;
                case "--classification-output":
                    classificationOutputPath = ReadValue(args, ref index, "--classification-output");
                    break;
                case "--suggest-patterns":
                    suggestPatterns = true;
                    break;
                case "--allow-layout-warnings":
                    allowLayoutWarnings = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--validation-report":
                    validationReportPath = ReadValue(args, ref index, "--validation-report");
                    break;
                case "--force":
                    force = true;
                    break;
                case "--help" or "-h":
                    showHelp = true;
                    break;
                default:
                    throw new CommandLineException($"Unknown option '{args[index]}'.");
            }
        }

        if (inspectLayout && auditReferences)
        {
            throw new CommandLineException("--inspect-layout and --audit-references cannot be combined.");
        }
        if ((classifySources ? 1 : 0) + (suggestPatterns ? 1 : 0) + (auditReferences ? 1 : 0) + (inspectLayout ? 1 : 0) > 1)
        {
            throw new CommandLineException("Choose only one inspection, audit, classification, or suggestion mode.");
        }
        if (suggestPatterns && string.IsNullOrWhiteSpace(sequencePath))
        {
            throw new CommandLineException("--suggest-patterns requires --sequence-path.");
        }
        if (suggestPatterns && (targetAll || manifestPath is not null))
        {
            throw new CommandLineException("--suggest-patterns cannot be combined with --target All or --manifest.");
        }
        if (classifySources && (targetAll || sequencePath is not null || manifestPath is not null))
        {
            throw new CommandLineException("--classify-sources cannot be combined with a generation selector.");
        }
        if (auditReferences && (targetAll || manifestPath is not null))
        {
            throw new CommandLineException("--audit-references cannot be combined with --target All or --manifest.");
        }
        var generationModes = (targetAll ? 1 : 0) + (sequencePath is null ? 0 : 1) + (manifestPath is null ? 0 : 1);
        if (!showHelp && !inspectLayout && !auditReferences && !classifySources && !suggestPatterns && generationModes != 1)
        {
            throw new CommandLineException(
                "Choose exactly one generation mode: --target All, --sequence-path <file.xsq>, or --manifest <file.json>.");
        }

        return new CommandLineOptions(
            targetAll,
            sequencePath,
            headCount,
            Path.GetFullPath(workspaceRoot),
            rgbEffectsPath,
            movingHeadGroupName,
            inspectLayout,
            auditReferences,
            allowLayoutWarnings,
            force,
            showHelp,
            configPath,
            headCountSpecified,
            allowLayoutWarnings,
            dryRun,
            validationReportPath,
            includeFixtures.Count == 0 ? null : includeFixtures,
            excludeFixtures.Count == 0 ? null : excludeFixtures,
            includeFixtureGroups.Count == 0 ? null : includeFixtureGroups,
            includeFixtureTags.Count == 0 ? null : includeFixtureTags,
            auditFormat,
            auditOutputPath,
            cueSheetPath,
            manifestPath,
            classifySources,
            classificationFormat,
            classificationOutputPath,
            suggestPatterns);
    }

    private static int ParseHeadCount(string value) =>
        int.TryParse(value, out var count) && HeadCountPolicy.IsSupported(count)
            ? count
            : throw new CommandLineException(
                $"Unsupported head count '{value}'. Use {HeadCountPolicy.RangeText}.");

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count || args[index].StartsWith('-'))
        {
            throw new CommandLineException($"Option '{option}' requires a value.");
        }

        return args[index];
    }
}

internal sealed class CommandLineException(string message) : Exception(message);