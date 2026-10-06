using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovingHeadSequencer.Configuration;

internal sealed record BatchManifest
{
    public IReadOnlyList<BatchManifestEntry> Sequences { get; init; } = [];

    public static BatchManifest Load(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Batch manifest was not found: {resolvedPath}", resolvedPath);
        }
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var manifest = JsonSerializer.Deserialize<BatchManifest>(File.ReadAllText(resolvedPath), options)
            ?? throw new InvalidDataException($"Batch manifest is empty: {resolvedPath}");
        if (manifest.Sequences.Count == 0)
        {
            throw new InvalidDataException($"Batch manifest '{resolvedPath}' has no sequences.");
        }

        var baseDirectory = Path.GetDirectoryName(resolvedPath)!;
        var normalized = manifest.Sequences.Select((entry, index) => entry.Normalize(baseDirectory, index)).ToArray();
        var duplicateOutputs = normalized
            .Where(entry => entry.OutputPath is not null)
            .GroupBy(entry => entry.OutputPath!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateOutputs.Length > 0)
        {
            throw new InvalidDataException(
                $"Batch manifest writes multiple entries to: {string.Join(", ", duplicateOutputs)}.");
        }
        return manifest with { Sequences = normalized };
    }
}

internal sealed record BatchManifestEntry
{
    public required string SequencePath { get; init; }
    public string? OutputPath { get; init; }
    public int? HeadCount { get; init; }
    public bool? AllowLayoutWarnings { get; init; }
    public FixtureProfileSettings? FixtureProfile { get; init; }
    public ValidationSettings? Validation { get; init; }
    public AuditSettings? Audit { get; init; }
    public FixtureSelectionSettings? FixtureSelection { get; init; }
    public string? RgbEffectsPath { get; init; }
    public string? MovingHeadGroupName { get; init; }
    public string? CueSheetPath { get; init; }

    public SequenceOverrideSettings ToOverride() => new()
    {
        HeadCount = HeadCount,
        AllowLayoutWarnings = AllowLayoutWarnings,
        FixtureProfile = FixtureProfile,
        Validation = Validation,
        Audit = Audit,
        FixtureSelection = FixtureSelection,
        RgbEffectsPath = RgbEffectsPath,
        MovingHeadGroupName = MovingHeadGroupName,
        CueSheetPath = CueSheetPath,
    };

    internal BatchManifestEntry Normalize(string baseDirectory, int index)
    {
        if (string.IsNullOrWhiteSpace(SequencePath))
        {
            throw new InvalidDataException($"Batch manifest entry {index + 1} has no sequencePath.");
        }
        return this with
        {
            SequencePath = Resolve(baseDirectory, SequencePath),
            OutputPath = ResolveOptional(baseDirectory, OutputPath),
            RgbEffectsPath = ResolveOptional(baseDirectory, RgbEffectsPath),
            CueSheetPath = ResolveOptional(baseDirectory, CueSheetPath),
        };
    }

    private static string Resolve(string baseDirectory, string path) =>
        Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(baseDirectory, path));

    private static string? ResolveOptional(string baseDirectory, string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Resolve(baseDirectory, path);
}