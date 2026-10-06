using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovingHeadSequencer.Configuration;

internal sealed record GenerationSettings
{
    public int HeadCount { get; init; } = 4;
    public bool AllowLayoutWarnings { get; init; }
    public FixtureProfileSettings FixtureProfile { get; init; } = new();
    public ValidationSettings Validation { get; init; } = new();
    public AuditSettings Audit { get; init; } = new();
    public FixtureSelectionSettings FixtureSelection { get; init; } = new();
    public string? RgbEffectsPath { get; init; }
    public string? MovingHeadGroupName { get; init; }
    public string? CueSheetPath { get; init; }
    public IReadOnlyDictionary<string, SequenceOverrideSettings> SequenceOverrides { get; init; } =
        new Dictionary<string, SequenceOverrideSettings>(StringComparer.OrdinalIgnoreCase);

    public static GenerationSettings Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new GenerationSettings();
        }

        var resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Configuration file was not found: {resolvedPath}", resolvedPath);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var settings = JsonSerializer.Deserialize<GenerationSettings>(File.ReadAllText(resolvedPath), options)
            ?? throw new InvalidDataException($"Configuration file is empty: {resolvedPath}");
        settings.Validate(resolvedPath);
        return settings;
    }

    public ResolvedGenerationSettings Resolve(
        int? cliHeadCount,
        bool? cliAllowLayoutWarnings,
        string? cliRgbEffectsPath,
        string? cliMovingHeadGroupName,
        FixtureSelectionSettings? cliFixtureSelection,
        string? cliCueSheetPath,
        SequenceOverrideSettings? invocationOverride,
        string? sequencePath)
    {
        var sequenceOverride = FindSequenceOverride(sequencePath);
        var resolved = new ResolvedGenerationSettings(
            cliHeadCount ?? invocationOverride?.HeadCount ?? sequenceOverride?.HeadCount ?? HeadCount,
            cliAllowLayoutWarnings ?? invocationOverride?.AllowLayoutWarnings ?? sequenceOverride?.AllowLayoutWarnings ?? AllowLayoutWarnings,
            invocationOverride?.FixtureProfile ?? sequenceOverride?.FixtureProfile ?? FixtureProfile,
            invocationOverride?.Validation ?? sequenceOverride?.Validation ?? Validation,
            invocationOverride?.Audit ?? sequenceOverride?.Audit ?? Audit,
            cliFixtureSelection ?? invocationOverride?.FixtureSelection ?? sequenceOverride?.FixtureSelection ?? FixtureSelection,
            cliRgbEffectsPath ?? invocationOverride?.RgbEffectsPath ?? sequenceOverride?.RgbEffectsPath ?? RgbEffectsPath,
            cliMovingHeadGroupName ?? invocationOverride?.MovingHeadGroupName ?? sequenceOverride?.MovingHeadGroupName ?? MovingHeadGroupName,
            cliCueSheetPath ?? invocationOverride?.CueSheetPath ?? sequenceOverride?.CueSheetPath ?? CueSheetPath);
        resolved.Validate(sequencePath ?? "project defaults");
        return resolved;
    }

    private SequenceOverrideSettings? FindSequenceOverride(string? sequencePath)
    {
        if (string.IsNullOrWhiteSpace(sequencePath))
        {
            return null;
        }
        var exact = SequenceOverrides.FirstOrDefault(pair =>
            pair.Key.Equals(sequencePath, StringComparison.OrdinalIgnoreCase));
        if (!exact.Equals(default(KeyValuePair<string, SequenceOverrideSettings>)))
        {
            return exact.Value;
        }
        var fileName = Path.GetFileName(sequencePath);
        return SequenceOverrides.FirstOrDefault(pair =>
            pair.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private void Validate(string source)
    {
        if (!HeadCountPolicy.IsSupported(HeadCount))
        {
            throw new InvalidDataException(
                $"Configuration '{source}' has unsupported headCount {HeadCount}; use {HeadCountPolicy.RangeText}.");
        }
        FixtureProfile.Validate(source);
        Validation.Validate(source);
        Audit.Validate(source);
    }
}

internal sealed record FixtureProfileSettings
{
    public string Name { get; init; } = "LegacySafe";
    public int PanTravelDegrees { get; init; } = 540;
    public int TiltTravelDegrees { get; init; } = 270;
    public AxisProfileSettings Pan { get; init; } = new(38, 212, 170, 130, 210, false);
    public AxisProfileSettings Tilt { get; init; } = new(38, 123, 50, 85, 100, false);
    public IReadOnlyDictionary<string, FixtureAxisOverrideSettings> Fixtures { get; init; } =
        new Dictionary<string, FixtureAxisOverrideSettings>(StringComparer.Ordinal);

    [JsonIgnore]
    public IReadOnlyList<ResolvedFixtureAxes> ResolvedFixtures { get; init; } = [];

    public void Validate(string source)
    {
        if (PanTravelDegrees is < 1 or > 1080 || TiltTravelDegrees is < 1 or > 1080)
        {
            throw new InvalidDataException(
                $"Configuration '{source}' has invalid fixture travel degrees; expected 1-1080 degrees.");
        }
        Pan.Validate(source, "pan");
        Tilt.Validate(source, "tilt");
        foreach (var (fixture, settings) in Fixtures)
        {
            settings.Pan?.Validate(source, $"fixtures.{fixture}.pan");
            settings.Tilt?.Validate(source, $"fixtures.{fixture}.tilt");
        }
    }

    public FixtureProfileSettings ResolveFixtures(IReadOnlyList<string> fixtureNames)
    {
        var unknown = Fixtures.Keys
            .Where(name => !fixtureNames.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidDataException(
                $"Fixture profile '{Name}' contains overrides for unselected fixtures: {string.Join(", ", unknown)}.");
        }
        return this with
        {
            ResolvedFixtures = fixtureNames.Select(name =>
            {
                Fixtures.TryGetValue(name, out var fixture);
                return new ResolvedFixtureAxes(name, fixture?.Pan ?? Pan, fixture?.Tilt ?? Tilt);
            }).ToArray(),
        };
    }

    public AxisProfileSettings PanFor(int index) =>
        ResolvedFixtures.Count == 0 ? Pan : ResolvedFixtures[index].Pan;

    public AxisProfileSettings TiltFor(int index) =>
        ResolvedFixtures.Count == 0 ? Tilt : ResolvedFixtures[index].Tilt;
}

internal sealed record FixtureAxisOverrideSettings
{
    public AxisProfileSettings? Pan { get; init; }
    public AxisProfileSettings? Tilt { get; init; }
}

internal sealed record ResolvedFixtureAxes(
    string FixtureName,
    AxisProfileSettings Pan,
    AxisProfileSettings Tilt);

internal sealed record AxisProfileSettings(
    int Minimum,
    int Maximum,
    int Park,
    int Outer,
    int Center,
    bool Inverted)
{
    public void Validate(string source, string axis)
    {
        if (Minimum is < 0 or > 255 || Maximum is < 0 or > 255 || Minimum >= Maximum)
        {
            throw new InvalidDataException($"Configuration '{source}' has an invalid {axis} range {Minimum}-{Maximum}.");
        }
        foreach (var (name, value) in new[] { ("park", Park), ("outer", Outer), ("center", Center) })
        {
            if (value < Minimum || value > Maximum)
            {
                throw new InvalidDataException(
                    $"Configuration '{source}' has {axis}.{name}={value} outside {Minimum}-{Maximum}.");
            }
        }
    }
}

internal sealed record ValidationSettings
{
    public int MinimumMovementDurationMs { get; init; } = 200;
    public int MaximumPanDelta { get; init; } = 174;
    public int MaximumTiltDelta { get; init; } = 85;
    public double MaximumDynamicMotionPercent { get; init; } = 70;
    public bool TreatWarningsAsErrors { get; init; }

    public void Validate(string source)
    {
        if (MinimumMovementDurationMs < 25 || MaximumPanDelta is < 1 or > 255 ||
            MaximumTiltDelta is < 1 or > 255 || MaximumDynamicMotionPercent is <= 0 or > 100)
        {
            throw new InvalidDataException($"Configuration '{source}' contains invalid validation limits.");
        }
    }
}

internal sealed record AuditSettings
{
    public int MinimumGapMs { get; init; } = 1000;
    public int NearDarkThresholdPercent { get; init; } = 10;
    public string OutputFormat { get; init; } = "table";

    public void Validate(string source)
    {
        if (MinimumGapMs < 0 || NearDarkThresholdPercent is < 0 or > 100 ||
            !Enum.TryParse<MovingHeadSequencer.Audit.AuditReportFormat>(OutputFormat, ignoreCase: true, out _))
        {
            throw new InvalidDataException($"Configuration '{source}' contains invalid audit settings.");
        }
    }
}

internal sealed record SequenceOverrideSettings
{
    public int? HeadCount { get; init; }
    public bool? AllowLayoutWarnings { get; init; }
    public FixtureProfileSettings? FixtureProfile { get; init; }
    public ValidationSettings? Validation { get; init; }
    public AuditSettings? Audit { get; init; }
    public FixtureSelectionSettings? FixtureSelection { get; init; }
    public string? RgbEffectsPath { get; init; }
    public string? MovingHeadGroupName { get; init; }
    public string? CueSheetPath { get; init; }
}

internal sealed record ResolvedGenerationSettings(
    int HeadCount,
    bool AllowLayoutWarnings,
    FixtureProfileSettings FixtureProfile,
    ValidationSettings Validation,
    AuditSettings Audit,
    FixtureSelectionSettings FixtureSelection,
    string? RgbEffectsPath,
    string? MovingHeadGroupName,
    string? CueSheetPath)
{
    public void Validate(string source)
    {
        if (!HeadCountPolicy.IsSupported(HeadCount))
        {
            throw new InvalidDataException(
                $"Resolved settings for '{source}' have unsupported headCount {HeadCount}; " +
                $"use {HeadCountPolicy.RangeText}.");
        }
        FixtureProfile.Validate(source);
        Validation.Validate(source);
        Audit.Validate(source);
    }
}

internal sealed record FixtureSelectionSettings
{
    public IReadOnlyList<string> IncludeNames { get; init; } = [];
    public IReadOnlyList<string> ExcludeNames { get; init; } = [];
    public IReadOnlyList<string> IncludeGroups { get; init; } = [];
    public IReadOnlyList<string> IncludeTags { get; init; } = [];

    public bool IsEmpty => IncludeNames.Count == 0 && ExcludeNames.Count == 0 &&
        IncludeGroups.Count == 0 && IncludeTags.Count == 0;
}