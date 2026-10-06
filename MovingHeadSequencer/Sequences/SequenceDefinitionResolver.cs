using System.Xml;
using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Sequences;

internal static class SequenceDefinitionResolver
{
    public static SequencePlan Create(string sequencePath, int headCount, bool layoutDriven)
        => Create(sequencePath, headCount, layoutDriven, new FixtureProfileSettings());

    public static SequencePlan Create(
        string sequencePath,
        int headCount,
        bool layoutDriven,
        FixtureProfileSettings fixtureProfile)
    {
        var resolvedPath = Path.GetFullPath(sequencePath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Sequence was not found: {resolvedPath}", resolvedPath);
        }
        if (!Path.GetExtension(resolvedPath).Equals(".xsq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("--sequence-path currently requires an .xsq file.");
        }

        var document = new XmlDocument();
        document.Load(resolvedPath);
        var song = document.SelectSingleNode("/xsequence/head/song")?.InnerText.Trim() ?? string.Empty;

        var plan = song switch
        {
            "A Christmas Storm" => StormSequenceDefinition.Create(headCount, layoutDriven, fixtureProfile),
            _ when song.StartsWith("All I Really Want For Christmas", StringComparison.OrdinalIgnoreCase) =>
                LilJonSequenceDefinition.Create(headCount, fixtureProfile),
            _ => GenericSequenceDefinition.Create(resolvedPath, headCount, fixtureProfile),
        };

        var directory = Path.GetDirectoryName(resolvedPath)
            ?? throw new InvalidDataException($"Sequence path has no parent directory: {resolvedPath}");
        var outputPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(resolvedPath)}-{headCount}MH.xsq");
        return plan with { SourceFileName = resolvedPath, OutputFileName = outputPath };
    }
}