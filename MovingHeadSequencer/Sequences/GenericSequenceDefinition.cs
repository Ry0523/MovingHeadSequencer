using System.Globalization;
using System.Xml;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Sequences;

internal static class GenericSequenceDefinition
{
    public static SequencePlan Create(
        string sequencePath,
        int headCount,
        FixtureProfileSettings fixtureProfile)
    {
        if (AggregateControlSequenceImporter.TryCreate(sequencePath, headCount, out var imported))
        {
            return imported;
        }

        var document = new XmlDocument();
        document.Load(sequencePath);
        var durationText = document.SelectSingleNode("/xsequence/head/sequenceDuration")?.InnerText;
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0)
        {
            throw new InvalidDataException(
                $"Sequence '{sequencePath}' does not declare a positive sequenceDuration.");
        }

        var declaredDuration = (int)Math.Ceiling((seconds * 1000d) / 25d) * 25;
        var lastEffectEnd = document.SelectNodes("//Effect[@endTime]")
            ?.Cast<XmlElement>()
            .Select(effect => int.TryParse(effect.GetAttribute("endTime"), out var end) ? end : 0)
            .DefaultIfEmpty()
            .Max() ?? 0;
        var duration = Math.Max(declaredDuration, lastEffectEnd);
        Cue Create(IReadOnlyList<string> keys) => Cue.Create(0, duration, keys, headCount);
        var panCues = new[] { Create(PatternFactory.Pan(PanPattern.Park, headCount, fixtureProfile)) };
        var tiltCues = new[] { Create(PatternFactory.Tilt(TiltPattern.Park, headCount, fixtureProfile)) };
        var dimmerCues = new[] { Create(PatternFactory.Dimmer(DimmerPattern.All0, headCount)) };
        var definitions = EffectDefinitions.CreateBase();
        definitions.AddCueDefinitions(panCues.Concat(tiltCues).Concat(dimmerCues));

        return new SequencePlan(
            SequenceKind.Generic,
            sequencePath,
            $"{Path.GetFileNameWithoutExtension(sequencePath)}-{headCount}MH.xsq",
            duration,
            0,
            definitions,
            panCues,
            tiltCues,
            dimmerCues,
            [],
            PreserveLegacyDimmer: false);
    }
}