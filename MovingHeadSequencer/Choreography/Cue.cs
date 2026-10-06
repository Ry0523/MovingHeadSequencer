namespace MovingHeadSequencer.Choreography;

internal sealed record Cue(int Start, int End, IReadOnlyList<string> Keys)
{
    public static Cue Create(int start, int end, IReadOnlyList<string> keys, int headCount)
    {
        if (keys.Count != headCount)
        {
            throw new InvalidDataException(
                $"Each cue must define {headCount} head positions; received {keys.Count}.");
        }
        if (start < 0 || end <= start)
        {
            throw new InvalidDataException($"Invalid cue interval {start}-{end}.");
        }

        return new Cue(start, end, keys);
    }
}

internal sealed record TrackEvent(int Start, int End, string Key);

internal static class TrackBuilder
{
    public static IReadOnlyDictionary<int, IReadOnlyList<TrackEvent>> Build(
        IReadOnlyList<Cue> cues,
        int headCount)
    {
        var tracks = Enumerable.Range(0, headCount)
            .ToDictionary(index => index, _ => new List<TrackEvent>());

        foreach (var cue in cues)
        {
            for (var nodeIndex = 0; nodeIndex < headCount; nodeIndex++)
            {
                var key = cue.Keys[nodeIndex];
                if (!string.IsNullOrEmpty(key))
                {
                    tracks[nodeIndex].Add(new TrackEvent(cue.Start, cue.End, key));
                }
            }
        }

        return tracks.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TrackEvent>)pair.Value);
    }
}