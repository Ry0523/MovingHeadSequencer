using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace MovingHeadSequencer.Timing;

internal enum TimingTrackKind
{
    Beat,
    Tempo,
    Phrase,
    Section,
    Marker,
}

internal sealed record TimingMark(int StartMs, int EndMs, string Label);

internal sealed record TimingLayer(int Index, IReadOnlyList<TimingMark> Marks);

internal sealed record TimingTrack(
    string Name,
    TimingTrackKind Kind,
    double Regularity,
    IReadOnlyList<TimingLayer> Layers);

internal sealed record BeatMarker(
    int TimeMs,
    int BeatNumber,
    int BarIndex,
    bool IsDownbeat);

internal sealed record BeatSource(
    string TrackName,
    TimingTrackKind Kind,
    double Bpm,
    double Confidence,
    int BeatsPerBar,
    IReadOnlyList<BeatMarker> Beats);

internal sealed record PhraseMarker(
    int StartMs,
    int EndMs,
    string Label,
    string TrackName,
    TimingTrackKind Kind);

internal sealed record TimingMap(
    int DurationMs,
    string? DefaultBeatSource,
    IReadOnlyList<BeatSource> BeatSources,
    IReadOnlyList<PhraseMarker> Phrases,
    IReadOnlyList<TimingTrack> Tracks);

internal static partial class XsqTimingMapReader
{
    public static TimingMap Read(string sequencePath)
    {
        var document = new XmlDocument();
        document.Load(sequencePath);
        var duration = (int)Math.Round(
            double.Parse(
                document.SelectSingleNode("/xsequence/head/sequenceDuration")?.InnerText ?? "0",
                CultureInfo.InvariantCulture) * 1000d);

        var tracks = document.SelectNodes("/xsequence/ElementEffects/Element[@type='timing']")
            ?.Cast<XmlElement>()
            .Select(ReadTrack)
            .ToArray() ?? [];
        var beatSources = tracks
            .Where(track => track.Kind is TimingTrackKind.Beat or TimingTrackKind.Tempo)
            .Select(CreateBeatSource)
            .Where(source => source.Beats.Count >= 8 && source.Bpm > 0)
            .OrderByDescending(ScoreBeatSource)
            .ThenBy(source => source.TrackName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var phrases = tracks
            .Where(track => track.Kind is TimingTrackKind.Phrase or TimingTrackKind.Section)
            .SelectMany(track => track.Layers.FirstOrDefault()?.Marks
                .Where(mark => !string.IsNullOrWhiteSpace(mark.Label))
                .Select(mark => new PhraseMarker(
                    mark.StartMs,
                    mark.EndMs,
                    mark.Label,
                    track.Name,
                    track.Kind)) ?? [])
            .OrderBy(marker => marker.StartMs)
            .ThenBy(marker => marker.Kind)
            .ToArray();

        return new TimingMap(
            duration,
            beatSources.FirstOrDefault()?.TrackName,
            beatSources,
            phrases,
            tracks);
    }

    private static TimingTrack ReadTrack(XmlElement element)
    {
        var layers = element.SelectNodes("./EffectLayer")
            ?.Cast<XmlElement>()
            .Select((layer, index) => new TimingLayer(
                index,
                layer.SelectNodes("./Effect")
                    ?.Cast<XmlElement>()
                    .Select(ReadMark)
                    .Where(mark => mark.EndMs >= mark.StartMs)
                    .OrderBy(mark => mark.StartMs)
                    .ToArray() ?? []))
            .ToArray() ?? [];
        var primary = layers.FirstOrDefault()?.Marks ?? [];
        var regularity = CalculateRegularity(primary);
        return new TimingTrack(
            element.GetAttribute("name"),
            Classify(layers, regularity),
            regularity,
            layers);
    }

    private static TimingMark ReadMark(XmlElement effect) => new(
        int.TryParse(effect.GetAttribute("startTime"), out var start) ? start : 0,
        int.TryParse(effect.GetAttribute("endTime"), out var end) ? end : 0,
        effect.GetAttribute("label").Trim());

    private static TimingTrackKind Classify(IReadOnlyList<TimingLayer> layers, double regularity)
    {
        var marks = layers.FirstOrDefault()?.Marks ?? [];
        if (marks.Count == 0)
        {
            return TimingTrackKind.Marker;
        }
        var labels = marks.Where(mark => !string.IsNullOrWhiteSpace(mark.Label)).ToArray();
        var numericRatio = labels.Length == 0
            ? 0
            : labels.Count(mark => BeatNumberRegex().IsMatch(mark.Label)) / (double)labels.Length;
        var tempoRatio = labels.Length == 0
            ? 0
            : labels.Count(mark => TempoLabelRegex().IsMatch(mark.Label)) / (double)labels.Length;
        if (marks.Count >= 16 && numericRatio >= 0.8 && regularity >= 0.75)
        {
            return TimingTrackKind.Beat;
        }
        if (marks.Count >= 16 && tempoRatio >= 0.8 && regularity >= 0.75)
        {
            return TimingTrackKind.Tempo;
        }
        if (layers.Count >= 2 && labels.Length > 0)
        {
            return TimingTrackKind.Phrase;
        }
        if (labels.Length >= Math.Max(2, marks.Count / 2) && marks.Count <= 128)
        {
            return TimingTrackKind.Section;
        }
        if (marks.Count >= 16 && regularity >= 0.88)
        {
            return TimingTrackKind.Beat;
        }
        return TimingTrackKind.Marker;
    }

    private static BeatSource CreateBeatSource(TimingTrack track)
    {
        var marks = track.Layers[0].Marks;
        var gaps = marks.Zip(marks.Skip(1), (left, right) => right.StartMs - left.StartMs)
            .Where(gap => gap > 0)
            .Order()
            .ToArray();
        var medianGap = Median(gaps);
        var bpm = medianGap <= 0 ? 0 : Math.Round(60000d / medianGap, 2);
        var parsedNumbers = marks
            .Select(mark => ParseBeatNumber(mark.Label))
            .ToArray();
        var labeledNumbers = parsedNumbers.Where(number => number is > 0 and <= 12).Select(number => number!.Value).ToArray();
        var beatsPerBar = labeledNumbers.Contains(1)
            ? Math.Max(2, labeledNumbers.Max())
            : 4;
        var barIndex = 0;
        var beats = marks.Select((mark, index) =>
        {
            var beatNumber = parsedNumbers[index] is > 0 and <= 12
                ? parsedNumbers[index]!.Value
                : (index % beatsPerBar) + 1;
            if (index > 0 && beatNumber == 1)
            {
                barIndex++;
            }
            return new BeatMarker(mark.StartMs, beatNumber, barIndex, beatNumber == 1);
        }).ToArray();
        var labelConfidence = labeledNumbers.Length >= marks.Count * 0.8
            ? 0.98
            : track.Kind == TimingTrackKind.Tempo ? 0.9 : 0.72;
        return new BeatSource(
            track.Name,
            track.Kind,
            bpm,
            Math.Round(Math.Min(labelConfidence, 0.6 + (track.Regularity * 0.4)), 2),
            beatsPerBar,
            beats);
    }

    private static double ScoreBeatSource(BeatSource source)
    {
        var kindScore = source.Kind == TimingTrackKind.Beat ? 100 : 80;
        var nameScore = source.TrackName.Equals("Beats", StringComparison.OrdinalIgnoreCase) ? 20 : 0;
        var downbeatScore = source.Beats.Any(beat => beat.IsDownbeat) ? 10 : 0;
        return kindScore + nameScore + downbeatScore + (source.Confidence * 10);
    }

    private static double CalculateRegularity(IReadOnlyList<TimingMark> marks)
    {
        var gaps = marks.Zip(marks.Skip(1), (left, right) => (double)(right.StartMs - left.StartMs))
            .Where(gap => gap > 0)
            .ToArray();
        if (gaps.Length < 2)
        {
            return 0;
        }
        var mean = gaps.Average();
        var variance = gaps.Average(gap => Math.Pow(gap - mean, 2));
        var coefficient = mean <= 0 ? 1 : Math.Sqrt(variance) / mean;
        return Math.Round(Math.Max(0, 1 - Math.Min(1, coefficient)), 3);
    }

    private static int? ParseBeatNumber(string label) =>
        int.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static double Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2d;
    }

    [GeneratedRegex("^[1-9]\\d*$", RegexOptions.CultureInvariant)]
    private static partial Regex BeatNumberRegex();

    [GeneratedRegex("^\\s*\\d+(?:\\.\\d+)?\\s*bpm\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TempoLabelRegex();
}