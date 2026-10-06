using System.Globalization;
using System.Text.RegularExpressions;

namespace MovingHeadSequencer.Choreography;

internal sealed partial class EffectDefinitions
{
    private readonly List<KeyValuePair<string, string>> definitions = [];
    private readonly HashSet<string> keys = new(StringComparer.Ordinal);

    public IReadOnlyList<KeyValuePair<string, string>> Items => definitions;

    public bool Contains(string key) => keys.Contains(key);

    public static EffectDefinitions CreateBase()
    {
        var result = new EffectDefinitions();
        result.AddHold("P130", 130);
        result.AddHold("P150", 150);
        result.AddHold("P170", 170);
        result.AddHold("P190", 190);
        result.AddHold("P210", 210);
        result.AddRamp("P170_130", 170, 130);
        result.AddRamp("P170_150", 170, 150);
        result.AddRamp("P170_190", 170, 190);
        result.AddRamp("P170_210", 170, 210);
        result.AddRamp("P130_170", 130, 170);
        result.AddRamp("P150_170", 150, 170);
        result.AddRamp("P190_170", 190, 170);
        result.AddRamp("P210_170", 210, 170);
        result.AddRamp("P130_210", 130, 210);
        result.AddRamp("P150_190", 150, 190);
        result.AddRamp("P190_150", 190, 150);
        result.AddRamp("P210_130", 210, 130);
        result.AddBounce("PB130_210_130", 130, 210, 130);
        result.AddBounce("PB150_190_150", 150, 190, 150);
        result.AddBounce("PB190_150_190", 190, 150, 190);
        result.AddBounce("PB210_130_210", 210, 130, 210);
        result.AddHold("T50", 50);
        result.AddHold("T85", 85);
        result.AddHold("T100", 100);
        result.AddRamp("T50_85", 50, 85);
        result.AddRamp("T50_100", 50, 100);
        result.AddRamp("T85_50", 85, 50);
        result.AddRamp("T100_50", 100, 50);
        result.AddBounce("TB50_85_50", 50, 85, 50);
        result.AddBounce("TB50_100_50", 50, 100, 50);
        result.AddBounce("TB85_50_85", 85, 50, 85);
        result.AddBounce("TB100_50_100", 100, 50, 100);
        return result;
    }

    public void AddOnHold(string key, int value) => Add(key, OnHold(value));

    public void AddOnRamp(string key, int start, int end) => Add(key, OnRamp(start, end));

    public void AddCueDefinitions(IEnumerable<Cue> cues)
    {
        foreach (var key in cues.SelectMany(cue => cue.Keys).Where(key => !string.IsNullOrEmpty(key)))
        {
            if (keys.Contains(key))
            {
                continue;
            }

            if (MotionCurveKey.TryParse(key, out var custom))
            {
                AddCustom(key, custom.Points);
                continue;
            }

            var match = HoldKeyRegex().Match(key);
            if (match.Success)
            {
                AddHold(key, Parse(match, 1));
                continue;
            }

            match = RampKeyRegex().Match(key);
            if (match.Success)
            {
                AddRamp(key, Parse(match, 1), Parse(match, 2));
                continue;
            }

            match = BounceKeyRegex().Match(key);
            if (match.Success)
            {
                AddBounce(key, Parse(match, 1), Parse(match, 2), Parse(match, 3));
                continue;
            }

            match = DimmerHoldKeyRegex().Match(key);
            if (match.Success)
            {
                AddOnHold(key, Parse(match, 1));
                continue;
            }

            match = DimmerRampKeyRegex().Match(key);
            if (match.Success)
            {
                AddOnRamp(key, Parse(match, 1), Parse(match, 2));
                continue;
            }

            throw new InvalidDataException($"Unsupported generated effect key '{key}'.");
        }
    }

    private void AddHold(string key, int value) => Add(key, DmxHold(value));

    private void AddRamp(string key, int start, int end) => Add(key, DmxRamp(start, end));

    private void AddBounce(string key, int start, int middle, int end) => Add(key, DmxBounce(start, middle, end));

    private void AddCustom(string key, IReadOnlyList<MotionCurvePoint> points) =>
        Add(key, DmxCustom(points));

    private void Add(string key, string value)
    {
        if (!keys.Add(key))
        {
            throw new InvalidOperationException($"Effect definition '{key}' already exists.");
        }
        definitions.Add(new KeyValuePair<string, string>(key, value));
    }

    private static int Parse(Match match, int group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static string DmxHold(int value) => $"E_NOTEBOOK1=Channels 1-10,E_SLIDER_DMX1={value}";

    private static string DmxRamp(int start, int end) =>
        $"E_NOTEBOOK1=Channels 1-10,E_VALUECURVE_DMX1=Active=TRUE|Id=ID_VALUECURVE_DMX1|Type=Ramp|Min=0.00|Max=255.00|P1={start}.00|P2={end}.00|RV=TRUE|";

    private static string DmxBounce(int start, int middle, int end) =>
        $"E_NOTEBOOK1=Channels 1-10,E_VALUECURVE_DMX1=Active=TRUE|Id=ID_VALUECURVE_DMX1|Type=Ramp Up/Down|Min=0.00|Max=255.00|P1={start}.00|P2={middle}.00|P3={end}.00|RV=TRUE|";

    private static string DmxCustom(IEnumerable<MotionCurvePoint> points)
    {
        var values = string.Join(';', points.Select(point => string.Create(
            CultureInfo.InvariantCulture,
            $"{point.Time:0.000}:{point.Value / 255d:0.000000}")));
        return "E_NOTEBOOK1=Channels 1-10," +
            "E_VALUECURVE_DMX1=Active=TRUE|Id=ID_VALUECURVE_DMX1|Type=Custom|" +
            $"Min=0.00|Max=255.00|RV=TRUE|Values={values}|";
    }

    private static string OnHold(int value) =>
        $"E_TEXTCTRL_Eff_On_Start={value},E_TEXTCTRL_Eff_On_End={value}";

    private static string OnRamp(int start, int end) =>
        $"E_TEXTCTRL_Eff_On_Start={start},E_TEXTCTRL_Eff_On_End={end}";

    [GeneratedRegex("^[PT](\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex HoldKeyRegex();

    [GeneratedRegex("^[PT](\\d+)_(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RampKeyRegex();

    [GeneratedRegex("^[PT]B(\\d+)_(\\d+)_(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BounceKeyRegex();

    [GeneratedRegex("^D(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DimmerHoldKeyRegex();

    [GeneratedRegex("^D(\\d+)_(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DimmerRampKeyRegex();
}