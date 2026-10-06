using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using MovingHeadSequencer.Configuration;

namespace MovingHeadSequencer.Audit;

internal enum AuditReportFormat
{
    Table,
    Csv,
    Json,
}

internal sealed record HeadActivityResult(string Track, double PositionPercent, double DynamicPercent);

internal sealed record ActivityAuditResult(
    ReferenceTier Tier,
    string FileName,
    int Duration,
    double AuthoredPositionPercent,
    double DynamicMotionPercent,
    double NoDynamicMotionPercent,
    double? PerHeadDynamicAveragePercent,
    double AggregateDynamicPercent,
    double? DimmerPositivePercent,
    double? VisibleMotionPercent,
    double? NearDarkMotionPercent,
    double? DimmedRepositionPercent,
    double? ShutterOpenPercent,
    IReadOnlyList<TimeInterval> MotionRestGaps,
    IReadOnlyList<TimeInterval> NearDarkWindows,
    string LongestGapContext,
    IReadOnlyList<HeadActivityResult> HeadActivity);

internal static partial class ReferenceActivityAuditor
{
    public static void WriteReport(
        string workspaceRoot,
        TextWriter output,
        string? sequencePath = null,
        AuditSettings? settings = null,
        AuditReportFormat format = AuditReportFormat.Table)
    {
        settings ??= new AuditSettings();
        IReadOnlyList<ReferenceSource> sources = sequencePath is null
            ? ReferenceSourceCatalog.Sources
            : [new ReferenceSource(ReferenceTier.Target, sequencePath)];
        var results = sources.Select(source => Audit(workspaceRoot, source, settings)).ToArray();

        switch (format)
        {
            case AuditReportFormat.Table:
                WriteTable(results, output);
                break;
            case AuditReportFormat.Csv:
                WriteCsv(results, output);
                break;
            case AuditReportFormat.Json:
                var options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonStringEnumConverter());
                output.Write(JsonSerializer.Serialize(results, options));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    public static ActivityAuditResult Audit(
        string workspaceRoot,
        ReferenceSource source,
        AuditSettings? auditSettings = null)
    {
        auditSettings ??= new AuditSettings();
        var path = Path.IsPathFullyQualified(source.FileName)
            ? source.FileName
            : Path.Combine(workspaceRoot, source.FileName);
        var document = new XmlDocument();
        document.Load(path);
        var effectDb = document.SelectNodes("/xsequence/EffectDB/Effect")?.Cast<XmlElement>().ToArray() ?? [];
        var declaredDuration = (int)Math.Round(
            double.Parse(document.SelectSingleNode("/xsequence/head/sequenceDuration")?.InnerText ?? "0", CultureInfo.InvariantCulture) * 1000d);
        var maximumEffectEnd = document.SelectNodes("/xsequence/ElementEffects//Effect[@endTime]")
            ?.Cast<XmlElement>()
            .Select(effect => int.TryParse(effect.GetAttribute("endTime"), out var end) ? end : 0)
            .DefaultIfEmpty()
            .Max() ?? 0;
        var duration = Math.Max(declaredDuration, maximumEffectEnd);

        var position = new List<TimeInterval>();
        var dynamic = new List<TimeInterval>();
        var aggregateDynamic = new List<TimeInterval>();
        var brightDimmer = new List<TimeInterval>();
        var nearDarkDimmer = new List<TimeInterval>();
        var shutterOpen = new List<TimeInterval>();
        var perHeadPosition = new Dictionary<string, List<TimeInterval>>(StringComparer.Ordinal);
        var perHeadDynamic = new Dictionary<string, List<TimeInterval>>(StringComparer.Ordinal);
        var hasKnownDimmer = false;
        var hasKnownShutter = false;

        foreach (var element in document.SelectNodes("/xsequence/ElementEffects/Element[@type='model']")?.Cast<XmlElement>() ?? [])
        {
            var modelName = element.GetAttribute("name");
            if (!MovingHeadNameRegex().IsMatch(modelName))
            {
                continue;
            }

            var directFixture = DirectFixtureNameRegex().IsMatch(modelName);
            var positionControl = PositionNameRegex().IsMatch(modelName);
            var dimmerControl = DimmerNameRegex().IsMatch(modelName);
            var shutterControl = ShutterNameRegex().IsMatch(modelName);
            foreach (var effect in element.SelectNodes(".//Effect")?.Cast<XmlElement>() ?? [])
            {
                if (!TryGetInterval(effect, out var interval))
                {
                    continue;
                }
                var effectSettings = GetEffectSettings(effect, effectDb);
                var effectName = effect.GetAttribute("name");
                var headTrack = GetHeadTrack(modelName, effect, directFixture, positionControl);
                var positionAuthored = effectName == "Moving Head" || positionControl ||
                    (directFixture && DirectPositionChannelRegex().IsMatch(effectSettings));
                if (positionAuthored)
                {
                    position.Add(interval);
                    AddTrackInterval(perHeadPosition, headTrack, interval);
                }

                var curveDriven = IsDynamicMovement(effectName, effectSettings, directFixture, positionControl);
                if (curveDriven)
                {
                    dynamic.Add(interval);
                    if (headTrack is null)
                    {
                        aggregateDynamic.Add(interval);
                    }
                    else
                    {
                        AddTrackInterval(perHeadDynamic, headTrack, interval);
                    }
                }

                if (dimmerControl)
                {
                    hasKnownDimmer = true;
                    AddIntensityInterval(
                        GetIntensityState(effectName, effectSettings, auditSettings.NearDarkThresholdPercent),
                        interval,
                        brightDimmer,
                        nearDarkDimmer);
                }
                else if (effectName == "Moving Head" && TryGetEmbeddedDimmer(effectSettings, out var dimmerValue))
                {
                    hasKnownDimmer = true;
                    AddIntensityInterval(
                        ClassifyPercent(dimmerValue, auditSettings.NearDarkThresholdPercent),
                        interval,
                        brightDimmer,
                        nearDarkDimmer);
                }

                if (shutterControl)
                {
                    hasKnownShutter = true;
                    if (GetIntensityState(effectName, effectSettings, 0) is IntensityState.Visible or IntensityState.Unknown)
                    {
                        shutterOpen.Add(interval);
                    }
                }
            }
        }

        var mergedDynamic = IntervalSet.Merge(dynamic);
        var positiveDimmer = IntervalSet.Merge(brightDimmer.Concat(nearDarkDimmer));
        var effectiveBright = ApplyShutter(brightDimmer, shutterOpen, hasKnownShutter);
        var effectiveNearDark = ApplyShutter(nearDarkDimmer, shutterOpen, hasKnownShutter);
        var knownVisibility = hasKnownDimmer || hasKnownShutter;
        var dark = knownVisibility
            ? IntervalSet.Merge(
                (hasKnownDimmer ? IntervalSet.Complement(positiveDimmer, duration) : [])
                .Concat(hasKnownShutter ? IntervalSet.Complement(shutterOpen, duration) : []))
            : [];
        var quietGaps = IntervalSet.Gaps(mergedDynamic, duration, auditSettings.MinimumGapMs);
        var longestGap = quietGaps.OrderByDescending(gap => gap.Duration).FirstOrDefault();
        var headResults = perHeadPosition.Keys.Union(perHeadDynamic.Keys, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new HeadActivityResult(
                name,
                Percent(IntervalSet.Coverage(perHeadPosition.GetValueOrDefault(name) ?? []), duration),
                Percent(IntervalSet.Coverage(perHeadDynamic.GetValueOrDefault(name) ?? []), duration)))
            .ToArray();

        return new ActivityAuditResult(
            source.Tier,
            source.FileName,
            duration,
            Percent(IntervalSet.Coverage(position), duration),
            Percent(IntervalSet.Coverage(mergedDynamic), duration),
            Percent(Math.Max(0, duration - IntervalSet.Coverage(mergedDynamic)), duration),
            headResults.Length == 0 ? null : Math.Round(headResults.Average(result => result.DynamicPercent), 1),
            Percent(IntervalSet.Coverage(aggregateDynamic), duration),
            hasKnownDimmer ? Percent(IntervalSet.Coverage(positiveDimmer), duration) : null,
            knownVisibility ? Percent(IntervalSet.IntersectionCoverage(mergedDynamic, effectiveBright), duration) : null,
            hasKnownDimmer ? Percent(IntervalSet.IntersectionCoverage(mergedDynamic, effectiveNearDark), duration) : null,
            knownVisibility ? Percent(IntervalSet.IntersectionCoverage(mergedDynamic, dark), duration) : null,
            hasKnownShutter ? Percent(IntervalSet.Coverage(shutterOpen), duration) : null,
            quietGaps,
            IntervalSet.Merge(effectiveNearDark),
            longestGap.Duration == 0 ? string.Empty : GetGapContext(document, longestGap),
            headResults);
    }

    private static void WriteTable(IReadOnlyList<ActivityAuditResult> results, TextWriter output)
    {
        output.WriteLine("Tier\tSequence\tPosition%\tDynamic%\tPerHeadAvg%\tAggregate%\tNoDynamic%\tDimmer+%\tVisibleMotion%\tNearDarkMotion%\tDimmedMove%\tShutterOpen%\tRestGaps\tLongestRest\tContext");
        foreach (var result in results)
        {
            var longest = result.MotionRestGaps.OrderByDescending(gap => gap.Duration).FirstOrDefault();
            output.WriteLine(string.Join('\t',
                result.Tier,
                result.FileName,
                Format(result.AuthoredPositionPercent),
                Format(result.DynamicMotionPercent),
                Format(result.PerHeadDynamicAveragePercent),
                Format(result.AggregateDynamicPercent),
                Format(result.NoDynamicMotionPercent),
                Format(result.DimmerPositivePercent),
                Format(result.VisibleMotionPercent),
                Format(result.NearDarkMotionPercent),
                Format(result.DimmedRepositionPercent),
                Format(result.ShutterOpenPercent),
                result.MotionRestGaps.Count,
                longest.Duration == 0 ? "0" : $"{longest.Start / 1000d:0.0}-{longest.End / 1000d:0.0}s",
                result.LongestGapContext));
        }
        WriteAverages(results, output);
    }

    private static void WriteCsv(IEnumerable<ActivityAuditResult> results, TextWriter output)
    {
        output.WriteLine("tier,sequence,durationMs,positionPercent,dynamicPercent,perHeadAveragePercent,aggregatePercent,noDynamicPercent,dimmerPositivePercent,visibleMotionPercent,nearDarkMotionPercent,dimmedRepositionPercent,shutterOpenPercent,restGapCount,longestRestStartMs,longestRestEndMs,context");
        foreach (var result in results)
        {
            var longest = result.MotionRestGaps.OrderByDescending(gap => gap.Duration).FirstOrDefault();
            output.WriteLine(string.Join(',',
                Csv(result.Tier.ToString()), Csv(result.FileName), result.Duration,
                Format(result.AuthoredPositionPercent), Format(result.DynamicMotionPercent),
                Format(result.PerHeadDynamicAveragePercent), Format(result.AggregateDynamicPercent),
                Format(result.NoDynamicMotionPercent), Format(result.DimmerPositivePercent),
                Format(result.VisibleMotionPercent), Format(result.NearDarkMotionPercent),
                Format(result.DimmedRepositionPercent), Format(result.ShutterOpenPercent),
                result.MotionRestGaps.Count, longest.Start, longest.End, Csv(result.LongestGapContext)));
        }
    }

    private static void WriteAverages(IReadOnlyList<ActivityAuditResult> results, TextWriter output)
    {
        foreach (var tier in results.Select(result => result.Tier).Distinct())
        {
            var tierResults = results.Where(result => result.Tier == tier).ToArray();
            output.WriteLine(
                $"{tier} averages: position={tierResults.Average(result => result.AuthoredPositionPercent):0.0}% " +
                $"dynamic={tierResults.Average(result => result.DynamicMotionPercent):0.0}% " +
                $"per-head={Average(tierResults.Select(result => result.PerHeadDynamicAveragePercent)):0.0}% " +
                $"aggregate={tierResults.Average(result => result.AggregateDynamicPercent):0.0}% " +
                $"visible-motion={Average(tierResults.Select(result => result.VisibleMotionPercent)):0.0}%");
        }
    }

    private static string? GetHeadTrack(string modelName, XmlElement effect, bool directFixture, bool positionControl)
    {
        if (directFixture)
        {
            return modelName;
        }
        var node = effect.ParentNode as XmlElement;
        return positionControl && node?.Name == "Node"
            ? $"{modelName}[{node.GetAttribute("index")}]"
            : null;
    }

    private static bool IsDynamicMovement(
        string effectName,
        string effectSettings,
        bool directFixture,
        bool positionControl)
    {
        if (effectName == "Moving Head")
        {
            return DynamicMovingHeadRegex().IsMatch(effectSettings);
        }
        if (effectName != "DMX" || (!directFixture && !positionControl))
        {
            return false;
        }

        foreach (Match match in DmxCurveRegex().Matches(effectSettings))
        {
            var channel = int.Parse(match.Groups["Channel"].Value, CultureInfo.InvariantCulture);
            if ((positionControl && channel != 1) || (directFixture && channel is not (10 or 12)))
            {
                continue;
            }
            var values = CurvePointRegex().Matches(match.Groups["Curve"].Value)
                .Select(point => point.Groups["Value"].Value)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count();
            if (values > 1)
            {
                return true;
            }
        }
        return false;
    }

    private static IntensityState GetIntensityState(string effectName, string effectSettings, int nearDarkThreshold)
    {
        if (effectName.Equals("Off", StringComparison.OrdinalIgnoreCase))
        {
            return IntensityState.Dark;
        }
        var values = OnIntensityRegex().Matches(effectSettings)
            .Select(match => int.Parse(match.Groups["Value"].Value, CultureInfo.InvariantCulture))
            .ToArray();
        return values.Length > 0
            ? ClassifyPercent(values.Max(), nearDarkThreshold)
            : IntensityState.Unknown;
    }

    private static bool TryGetEmbeddedDimmer(string effectSettings, out int value)
    {
        var match = EmbeddedDimmerRegex().Match(effectSettings);
        value = match.Success
            ? int.Parse(match.Groups["Value"].Value, CultureInfo.InvariantCulture)
            : 0;
        return match.Success;
    }

    private static IntensityState ClassifyPercent(int value, int nearDarkThreshold) => value switch
    {
        <= 0 => IntensityState.Dark,
        _ when value <= nearDarkThreshold => IntensityState.NearDark,
        _ => IntensityState.Visible,
    };

    private static void AddIntensityInterval(
        IntensityState state,
        TimeInterval interval,
        ICollection<TimeInterval> bright,
        ICollection<TimeInterval> nearDark)
    {
        if (state is IntensityState.Visible or IntensityState.Unknown)
        {
            bright.Add(interval);
        }
        else if (state == IntensityState.NearDark)
        {
            nearDark.Add(interval);
        }
    }

    private static IReadOnlyList<TimeInterval> ApplyShutter(
        IEnumerable<TimeInterval> intervals,
        IEnumerable<TimeInterval> shutterOpen,
        bool hasKnownShutter) =>
        hasKnownShutter ? IntervalSet.Intersect(intervals, shutterOpen) : IntervalSet.Merge(intervals);

    private static void AddTrackInterval(
        IDictionary<string, List<TimeInterval>> tracks,
        string? track,
        TimeInterval interval)
    {
        if (track is null)
        {
            return;
        }
        if (!tracks.TryGetValue(track, out var intervals))
        {
            intervals = [];
            tracks.Add(track, intervals);
        }
        intervals.Add(interval);
    }

    private static bool TryGetInterval(XmlElement effect, out TimeInterval interval)
    {
        if (int.TryParse(effect.GetAttribute("startTime"), out var start) &&
            int.TryParse(effect.GetAttribute("endTime"), out var end) && end > start)
        {
            interval = new TimeInterval(start, end);
            return true;
        }
        interval = default;
        return false;
    }

    private static string GetEffectSettings(XmlElement effect, IReadOnlyList<XmlElement> effectDb) =>
        int.TryParse(effect.GetAttribute("ref"), out var reference) && reference >= 0 && reference < effectDb.Count
            ? effectDb[reference].InnerText
            : string.Empty;

    private static string GetGapContext(XmlDocument document, TimeInterval gap)
    {
        var midpoint = gap.Start + (gap.Duration / 2);
        return string.Join(" / ",
            document.SelectNodes("/xsequence/ElementEffects/Element[@type='timing']//Effect[@label]")
                ?.Cast<XmlElement>()
                .Where(effect => int.TryParse(effect.GetAttribute("startTime"), out var start) &&
                                 int.TryParse(effect.GetAttribute("endTime"), out var end) &&
                                 start <= midpoint && end > midpoint)
                .Select(effect => effect.GetAttribute("label").Trim())
                .Where(label => label.Length is > 0 and <= 60)
                .Distinct(StringComparer.Ordinal)
                .Take(3) ?? []);
    }

    private static double Percent(int covered, int duration) =>
        duration == 0 ? 0 : Math.Round(100d * covered / duration, 1);

    private static string Format(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Format(double? value) =>
        value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "unknown";

    private static double Average(IEnumerable<double?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? 0 : known.Average();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private enum IntensityState
    {
        Unknown,
        Dark,
        NearDark,
        Visible,
    }

    [GeneratedRegex("(moving head|(^|[^a-z])mh([^a-z]|$)|mover|dmxmovinghead|dmx head|lempa)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovingHeadNameRegex();

    [GeneratedRegex("(^Mover\\d|^MH-\\d|DmxMovingHead|^DMX Head)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectFixtureNameRegex();

    [GeneratedRegex("Pan|Tilt", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositionNameRegex();

    [GeneratedRegex("Dimmer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimmerNameRegex();

    [GeneratedRegex("Shutter", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShutterNameRegex();

    [GeneratedRegex("(?:SLIDER|VALUECURVE)_DMX(?:10|12)=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectPositionChannelRegex();

    [GeneratedRegex("VALUECURVE_MH|MHPathDef=.+|PanOffset VC|Tilt VC", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DynamicMovingHeadRegex();

    [GeneratedRegex("E_VALUECURVE_DMX(?<Channel>\\d+)=(?<Curve>[^,]*)", RegexOptions.CultureInvariant)]
    private static partial Regex DmxCurveRegex();

    [GeneratedRegex("P\\d+=(?<Value>-?\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex CurvePointRegex();

    [GeneratedRegex("E_TEXTCTRL_Eff_On_(?:Start|End)=(?<Value>\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex OnIntensityRegex();

    [GeneratedRegex("Dimmer[:=](?<Value>\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedDimmerRegex();
}
