using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Sequences;

internal static partial class AggregateControlSequenceImporter
{
    public static bool TryCreate(string sequencePath, int headCount, out SequencePlan plan)
    {
        var document = new XmlDocument();
        document.Load(sequencePath);
        var panModel = FindControl(document, "mhpan");
        var tiltModel = FindControl(document, "mhtilt");
        var panNodes = ReadNodes(panModel);
        var tiltNodes = ReadNodes(tiltModel);
        if (panNodes.Length < 2 || panNodes.Length != tiltNodes.Length)
        {
            plan = null!;
            return false;
        }

        var effectDb = document.SelectNodes("/xsequence/EffectDB/Effect")
            ?.Cast<XmlElement>()
            .ToArray() ?? [];
        var duration = ReadDuration(document);
        var sourceIndexes = Enumerable.Range(0, headCount)
            .Select(index => (int)Math.Round(
                (panNodes.Length - 1d) * index / (headCount - 1d),
                MidpointRounding.AwayFromZero))
            .ToArray();
        var sourcePan = panNodes.Select(node => ReadAxisTrack(node, effectDb)).ToArray();
        var sourceTilt = tiltNodes.Select(node => ReadAxisTrack(node, effectDb)).ToArray();
        var panTracks = sourceIndexes.Select(index => sourcePan[index]).ToArray();
        var tiltTracks = sourceIndexes.Select(index => sourceTilt[index]).ToArray();
        var panPark = MidpointPark(panTracks, 70);
        var tiltPark = PreferredPark(tiltTracks, 70);
        var panCues = BuildAxisCues(panTracks, duration, 'P', panPark, headCount);
        var tiltCues = BuildAxisCues(tiltTracks, duration, 'T', tiltPark, headCount);

        var dimmerModel = FindControl(document, "mhdimmer", "mhdimmers");
        var sourceDimmerNodes = ReadNodes(dimmerModel);
        var rootDimmer = ReadRootIntensityTrack(dimmerModel, effectDb);
        var dimmerTracks = Enumerable.Range(0, headCount).Select(index =>
        {
            var sourceIndex = sourceDimmerNodes.Length == 0
                ? -1
                : (int)Math.Round(
                    (sourceDimmerNodes.Length - 1d) * index / (headCount - 1d),
                    MidpointRounding.AwayFromZero);
            var node = sourceIndex < 0
                ? Array.Empty<Primitive>()
                : ReadNodeIntensityTrack(sourceDimmerNodes[sourceIndex], effectDb);
            return rootDimmer.Concat(node).OrderBy(segment => segment.Start).ToArray();
        }).ToArray();
        var dimmerCues = BuildIntensityCues(dimmerTracks, duration, headCount);

        var shutterModel = FindControl(document, "mhshutter", "mhshutters");
        var shutterEvents = ReadShutterEvents(shutterModel, effectDb, duration);
        var profile = new FixtureProfileSettings
        {
            Name = "Imported XSQ controls",
            Pan = CreateAxisProfile(panTracks, panPark),
            Tilt = CreateAxisProfile(tiltTracks, tiltPark),
        };
        var definitions = EffectDefinitions.CreateBase();
        definitions.AddCueDefinitions(panCues.Concat(tiltCues).Concat(dimmerCues));
        if (shutterEvents.Length > 0 && !definitions.Contains("D100"))
        {
            definitions.AddOnHold("D100", 100);
        }

        plan = new SequencePlan(
            SequenceKind.Generic,
            sequencePath,
            $"{Path.GetFileNameWithoutExtension(sequencePath)}-{headCount}MH.xsq",
            duration,
            0,
            definitions,
            panCues,
            tiltCues,
            dimmerCues,
            shutterEvents,
            PreserveLegacyDimmer: false,
            ImportedSourceControls: true,
            FixtureProfileOverride: profile,
            FrameIntervalMs: 1);
        return true;
    }

    private static XmlElement? FindControl(XmlDocument document, params string[] normalizedNames)
    {
        var names = normalizedNames.ToHashSet(StringComparer.Ordinal);
        return document.SelectNodes("/xsequence/ElementEffects/Element[@type='model']")
            ?.Cast<XmlElement>()
            .FirstOrDefault(element => names.Contains(Normalize(element.GetAttribute("name"))));
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static XmlElement[] ReadNodes(XmlElement? model) => model?
        .SelectNodes("./Strand/Node")
        ?.Cast<XmlElement>()
        .OrderBy(node => ParseInt(node.GetAttribute("index")))
        .ToArray() ?? [];

    private static Primitive[] ReadAxisTrack(XmlElement node, IReadOnlyList<XmlElement> effectDb)
    {
        var result = new List<Primitive>();
        foreach (var effect in node.SelectNodes("./Effect")?.Cast<XmlElement>() ?? [])
        {
            if (!TryReadInterval(effect, out var start, out var end))
            {
                continue;
            }
            var settings = GetSettings(effect, effectDb);
            var curve = DmxCurveRegex().Match(settings);
            if (!curve.Success)
            {
                var hold = DmxHoldRegex().Match(settings);
                result.Add(new Primitive(start, end, ParseDouble(hold.Groups["Value"].Value),
                    ParseDouble(hold.Groups["Value"].Value)));
                continue;
            }

            var definition = curve.Groups["Definition"].Value;
            var type = CurveTypeRegex().Match(definition).Groups["Value"].Value;
            var first = ReadCurvePoint(definition, "P1");
            var second = ReadCurvePoint(definition, "P2", first);
            var third = ReadCurvePoint(definition, "P3", first);
            switch (type)
            {
                case "Ramp Up/Down":
                    var midpoint = start + ((end - start) / 2);
                    AddPrimitive(result, start, midpoint, first, second);
                    AddPrimitive(result, midpoint, end, second, third);
                    break;
                case "Saw Tooth":
                    var cycles = Math.Max(1, (int)Math.Round(third, MidpointRounding.AwayFromZero));
                    for (var cycle = 0; cycle < cycles; cycle++)
                    {
                        var cycleStart = start + (int)Math.Round(
                            (end - start) * cycle / (double)cycles,
                            MidpointRounding.AwayFromZero);
                        var cycleEnd = start + (int)Math.Round(
                            (end - start) * (cycle + 1) / (double)cycles,
                            MidpointRounding.AwayFromZero);
                        AddPrimitive(result, cycleStart, cycleEnd, first, second);
                    }
                    break;
                default:
                    AddPrimitive(result, start, end, first, second);
                    break;
            }
        }
        return [.. result.OrderBy(segment => segment.Start)];
    }

    private static Primitive[] ReadRootIntensityTrack(
        XmlElement? model,
        IReadOnlyList<XmlElement> effectDb)
    {
        if (model is null)
        {
            return [];
        }
        return model.SelectNodes("./EffectLayer/Effect")
            ?.Cast<XmlElement>()
            .SelectMany(effect => ReadIntensityEffect(effect, effectDb, groupEffect: true))
            .OrderBy(segment => segment.Start)
            .ToArray() ?? [];
    }

    private static Primitive[] ReadNodeIntensityTrack(
        XmlElement node,
        IReadOnlyList<XmlElement> effectDb) => node
        .SelectNodes("./Effect")
        ?.Cast<XmlElement>()
        .SelectMany(effect => ReadIntensityEffect(effect, effectDb, groupEffect: false))
        .OrderBy(segment => segment.Start)
        .ToArray() ?? [];

    private static IEnumerable<Primitive> ReadIntensityEffect(
        XmlElement effect,
        IReadOnlyList<XmlElement> effectDb,
        bool groupEffect)
    {
        if (!TryReadInterval(effect, out var start, out var end))
        {
            return [];
        }
        if (!effect.GetAttribute("name").Equals("On", StringComparison.OrdinalIgnoreCase))
        {
            return groupEffect ? [new Primitive(start, end, 100, 100)] : [];
        }

        var settings = GetSettings(effect, effectDb);
        var first = ReadSetting(settings, OnStartRegex(), 100);
        var last = ReadSetting(settings, OnEndRegex(), first);
        var fadeIn = (int)Math.Round(ReadSetting(settings, FadeInRegex(), 0) * 1000d);
        var fadeOut = (int)Math.Round(ReadSetting(settings, FadeOutRegex(), 0) * 1000d);
        var boundaries = new SortedSet<int> { start, end };
        if (fadeIn > 0 && start + fadeIn < end)
        {
            boundaries.Add(start + fadeIn);
        }
        if (fadeOut > 0 && end - fadeOut > start)
        {
            boundaries.Add(end - fadeOut);
        }
        var points = boundaries.ToArray();
        var result = new List<Primitive>();
        for (var index = 0; index < points.Length - 1; index++)
        {
            var left = points[index];
            var right = points[index + 1];
            AddPrimitive(
                result,
                left,
                right,
                EvaluateIntensity(left, start, end, first, last, fadeIn, fadeOut),
                EvaluateIntensity(right, start, end, first, last, fadeIn, fadeOut));
        }
        return result;
    }

    private static double EvaluateIntensity(
        int time,
        int start,
        int end,
        double first,
        double last,
        int fadeIn,
        int fadeOut)
    {
        var progress = end <= start ? 0 : (double)(time - start) / (end - start);
        var value = Lerp(first, last, Math.Clamp(progress, 0, 1));
        var opacity = 1d;
        if (fadeIn > 0)
        {
            opacity = Math.Min(opacity, Math.Clamp((double)(time - start) / fadeIn, 0, 1));
        }
        if (fadeOut > 0)
        {
            opacity = Math.Min(opacity, Math.Clamp((double)(end - time) / fadeOut, 0, 1));
        }
        return value * opacity;
    }

    private static Cue[] BuildAxisCues(
        IReadOnlyList<Primitive>[] tracks,
        int duration,
        char prefix,
        int park,
        int headCount)
    {
        var boundaries = GetBoundaries(tracks, duration);
        var cues = new List<Cue>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            var keys = tracks.Select(track => BuildKey(track, start, end, prefix, park)).ToArray();
            AddCue(cues, start, end, keys, headCount);
        }
        return [.. cues];
    }

    private static Cue[] BuildIntensityCues(
        IReadOnlyList<Primitive>[] tracks,
        int duration,
        int headCount)
    {
        var boundaries = GetBoundaries(tracks, duration);
        var cues = new List<Cue>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            var keys = tracks.Select(track =>
            {
                var first = Round(EvaluateMaximum(track, start, end, start));
                var last = Round(EvaluateMaximum(track, start, end, end));
                return first == last ? $"D{first}" : $"D{first}_{last}";
            }).ToArray();
            AddCue(cues, start, end, keys, headCount);
        }
        return [.. cues];
    }

    private static string BuildKey(
        IReadOnlyList<Primitive> track,
        int start,
        int end,
        char prefix,
        int fallback)
    {
        var segment = track.FirstOrDefault(candidate => candidate.Start <= start && candidate.End >= end);
        if (segment is null)
        {
            return $"{prefix}{fallback}";
        }
        var first = Round(segment.Evaluate(start));
        var last = Round(segment.Evaluate(end));
        return first == last ? $"{prefix}{first}" : $"{prefix}{first}_{last}";
    }

    private static double EvaluateMaximum(
        IReadOnlyList<Primitive> track,
        int intervalStart,
        int intervalEnd,
        int time)
    {
        var active = track.Where(segment => segment.Start <= intervalStart && segment.End >= intervalEnd).ToArray();
        return active.Length == 0 ? 0 : active.Max(segment => segment.Evaluate(time));
    }

    private static int[] GetBoundaries(IEnumerable<IReadOnlyList<Primitive>> tracks, int duration) => tracks
        .SelectMany(track => track.SelectMany(segment => new[] { segment.Start, segment.End }))
        .Append(0)
        .Append(duration)
        .Where(time => time >= 0 && time <= duration)
        .Distinct()
        .Order()
        .ToArray();

    private static void AddCue(
        ICollection<Cue> cues,
        int start,
        int end,
        IReadOnlyList<string> keys,
        int headCount)
    {
        if (end > start)
        {
            cues.Add(Cue.Create(start, end, keys, headCount));
        }
    }

    private static RootEffectEvent[] ReadShutterEvents(
        XmlElement? model,
        IReadOnlyList<XmlElement> effectDb,
        int duration)
    {
        if (model is null)
        {
            return [new RootEffectEvent(0, duration, "D100")];
        }
        var intervals = model.SelectNodes(".//Effect[@name='On']")
            ?.Cast<XmlElement>()
            .Where(effect => TryReadInterval(effect, out _, out _))
            .Select(effect => new RootEffectEvent(
                ParseInt(effect.GetAttribute("startTime")),
                Math.Min(duration, ParseInt(effect.GetAttribute("endTime"))),
                "D100"))
            .Where(effect => effect.End > effect.Start)
            .OrderBy(effect => effect.Start)
            .ToArray() ?? [];
        if (intervals.Length == 0)
        {
            return [];
        }
        var merged = new List<RootEffectEvent>();
        foreach (var interval in intervals)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, interval.End) };
            }
            else
            {
                merged.Add(interval);
            }
        }
        return [.. merged];
    }

    private static AxisProfileSettings CreateAxisProfile(
        IEnumerable<IReadOnlyList<Primitive>> tracks,
        int park)
    {
        var values = tracks.SelectMany(track => track.SelectMany(segment =>
                new[] { segment.StartValue, segment.EndValue }))
            .Append(park)
            .ToArray();
        var minimum = Math.Clamp((int)Math.Floor(values.Min()), 0, 254);
        var maximum = Math.Clamp((int)Math.Ceiling(values.Max()), minimum + 1, 255);
        return new AxisProfileSettings(
            minimum,
            maximum,
            Math.Clamp(park, minimum, maximum),
            minimum,
            maximum,
            false);
    }

    private static int PreferredPark(IEnumerable<IReadOnlyList<Primitive>> tracks, int preferred)
    {
        var values = tracks.SelectMany(track => track.SelectMany(segment =>
            new[] { Round(segment.StartValue), Round(segment.EndValue) })).ToArray();
        return values.Length == 0 ? preferred : Math.Clamp(preferred, values.Min(), values.Max());
    }

    private static int MidpointPark(IEnumerable<IReadOnlyList<Primitive>> tracks, int fallback)
    {
        var values = tracks.SelectMany(track => track.SelectMany(segment =>
            new[] { segment.StartValue, segment.EndValue })).ToArray();
        return values.Length == 0
            ? fallback
            : Round((values.Min() + values.Max()) / 2d);
    }

    private static int ReadDuration(XmlDocument document)
    {
        var text = document.SelectSingleNode("/xsequence/head/sequenceDuration")?.InnerText;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            throw new InvalidDataException("Imported sequence does not declare a positive sequenceDuration.");
        }
        return (int)Math.Round(seconds * 1000d, MidpointRounding.AwayFromZero);
    }

    private static bool TryReadInterval(XmlElement effect, out int start, out int end)
    {
        start = ParseInt(effect.GetAttribute("startTime"));
        end = ParseInt(effect.GetAttribute("endTime"));
        return end > start;
    }

    private static string GetSettings(XmlElement effect, IReadOnlyList<XmlElement> effectDb) =>
        int.TryParse(effect.GetAttribute("ref"), out var reference) && reference >= 0 && reference < effectDb.Count
            ? effectDb[reference].InnerText
            : string.Empty;

    private static double ReadCurvePoint(string definition, string key, double fallback = 0)
    {
        var match = Regex.Match(
            definition,
            $"(?:^|\\|){Regex.Escape(key)}=(?<Value>-?[\\d.]+)",
            RegexOptions.CultureInvariant);
        return match.Success ? ParseDouble(match.Groups["Value"].Value) : fallback;
    }

    private static double ReadSetting(string settings, Regex expression, double fallback)
    {
        var match = expression.Match(settings);
        return match.Success ? ParseDouble(match.Groups["Value"].Value) : fallback;
    }

    private static void AddPrimitive(
        ICollection<Primitive> target,
        int start,
        int end,
        double first,
        double last)
    {
        if (end > start)
        {
            target.Add(new Primitive(start, end, Clamp(first), Clamp(last)));
        }
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static int Round(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 255), MidpointRounding.AwayFromZero);

    private static double Clamp(double value) => Math.Clamp(value, 0, 255);

    private static double Lerp(double first, double last, double progress) =>
        first + ((last - first) * progress);

    private sealed record Primitive(int Start, int End, double StartValue, double EndValue)
    {
        public double Evaluate(int time)
        {
            if (End <= Start || StartValue == EndValue)
            {
                return StartValue;
            }
            var progress = Math.Clamp((double)(time - Start) / (End - Start), 0, 1);
            return Lerp(StartValue, EndValue, progress);
        }
    }

    [GeneratedRegex("E_VALUECURVE_DMX1=(?<Definition>Active=TRUE\\|[^,]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DmxCurveRegex();

    [GeneratedRegex("(?:^|,)E_SLIDER_DMX1=(?<Value>-?\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DmxHoldRegex();

    [GeneratedRegex("(?:^|\\|)Type=(?<Value>[^|]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurveTypeRegex();

    [GeneratedRegex("(?:^|,)E_TEXTCTRL_Eff_On_Start=(?<Value>[\\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnStartRegex();

    [GeneratedRegex("(?:^|,)E_TEXTCTRL_Eff_On_End=(?<Value>[\\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnEndRegex();

    [GeneratedRegex("(?:^|,)T_TEXTCTRL_Fadein=(?<Value>[\\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FadeInRegex();

    [GeneratedRegex("(?:^|,)T_TEXTCTRL_Fadeout=(?<Value>[\\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FadeOutRegex();
}
