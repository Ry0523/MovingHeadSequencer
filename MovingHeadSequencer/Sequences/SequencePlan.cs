using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Sequences;

internal enum SequenceKind
{
    Storm,
    LilJon,
    Generic,
}

internal sealed record RootEffectEvent(int Start, int End, string Key)
{
    public static RootEffectEvent[] FromDimmerCues(IReadOnlyList<Cue> dimmerCues)
    {
        var events = new List<RootEffectEvent>();
        foreach (var cue in dimmerCues.Where(HasVisibleIntensity))
        {
            if (events.Count > 0 && events[^1].End == cue.Start)
            {
                events[^1] = events[^1] with { End = cue.End };
            }
            else
            {
                events.Add(new RootEffectEvent(cue.Start, cue.End, "D100"));
            }
        }
        return [.. events];
    }

    private static bool HasVisibleIntensity(Cue cue) =>
        cue.Keys.Any(key => !string.IsNullOrEmpty(key) && key != "D0");
}

internal sealed record SequencePlan(
    SequenceKind Kind,
    string SourceFileName,
    string OutputFileName,
    int Duration,
    int Palette,
    EffectDefinitions Definitions,
    IReadOnlyList<Cue> PanCues,
    IReadOnlyList<Cue> TiltCues,
    IReadOnlyList<Cue> DimmerCues,
    IReadOnlyList<RootEffectEvent> ShutterEvents,
    bool PreserveLegacyDimmer,
    bool ImportedSourceControls = false,
    FixtureProfileSettings? FixtureProfileOverride = null,
    int FrameIntervalMs = 25);