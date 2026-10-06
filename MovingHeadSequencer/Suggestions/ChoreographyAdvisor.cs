using System.Xml;
using System.Text.RegularExpressions;
using MovingHeadSequencer.Audit;

namespace MovingHeadSequencer.Suggestions;

internal sealed record ChoreographySuggestion(
    string Context,
    string Pattern,
    string Intensity,
    string Rationale);

internal static class ChoreographyAdvisor
{
    public static IReadOnlyList<ChoreographySuggestion> Suggest(
        IEnumerable<string> labels,
        ActivityAuditResult audit)
    {
        var suggestions = labels
            .Select(label => Suggest(label, audit.DynamicMotionPercent))
            .DistinctBy(suggestion => new { suggestion.Context, suggestion.Pattern })
            .ToList();
        if (suggestions.Count == 0)
        {
            suggestions.Add(Suggest("unlabeled section", audit.DynamicMotionPercent));
        }
        return suggestions;
    }

    public static IReadOnlyList<string> ReadTimingLabels(string path)
    {
        var document = new XmlDocument();
        document.Load(path);
        return document.SelectNodes("/xsequence/ElementEffects/Element[@type='timing']//Effect[@label]")
            ?.Cast<XmlElement>()
            .Select(effect => effect.GetAttribute("label").Trim())
            .Where(label => label.Length is > 0 and <= 80)
            .Where(IsSectionLike)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray() ?? [];
    }

    public static void WriteReport(
        string sequencePath,
        ActivityAuditResult audit,
        IReadOnlyList<ChoreographySuggestion> suggestions,
        TextWriter output)
    {
        output.WriteLine($"Suggestions for {Path.GetFileName(sequencePath)}");
        output.WriteLine(
            $"Measured dynamic motion: {audit.DynamicMotionPercent:0.0}%; " +
            $"no dynamic motion: {audit.NoDynamicMotionPercent:0.0}%.");
        foreach (var suggestion in suggestions)
        {
            output.WriteLine($"{suggestion.Context}\t{suggestion.Pattern}\t{suggestion.Intensity}\t{suggestion.Rationale}");
        }
    }

    private static ChoreographySuggestion Suggest(string label, double dynamicPercent)
    {
        var normalized = label.ToLowerInvariant();
        var densityNote = dynamicPercent > 60
            ? " Existing motion is dense; prefer a hold or blackout before this cue."
            : string.Empty;
        if (ContainsAny(normalized, "intro", "verse", "break", "bridge", "outro", "quiet"))
        {
            return new ChoreographySuggestion(
                label, "Park or static fan", "Dark or 0-45%", "Creates contrast and leaves room for lyrics." + densityNote);
        }
        if (ContainsAny(normalized, "build", "rise", "riser", "firework"))
        {
            return new ChoreographySuggestion(
                label, "Tilt-only rise", "Fade 45-75%", "Vertical motion supports a build without spending a full pan sweep." + densityNote);
        }
        if (ContainsAny(normalized, "chorus", "drop", "dance", "finale"))
        {
            return new ChoreographySuggestion(
                label, "Mirrored fan or one phrase bounce", "70-100%", "Use broad symmetric motion for the section arrival, then hold." + densityNote);
        }
        if (ContainsAny(normalized, "yeah", "hey", "hit", "impact", "oh"))
        {
            return new ChoreographySuggestion(
                label, "Stable pose with intensity accent", "Short 100% hit", "Intensity punctuates short calls more cleanly than mechanical snaps." + densityNote);
        }
        return new ChoreographySuggestion(
            label, "Phrase ramp", "45-75%", "Use one movement across the phrase and preserve a rest afterward." + densityNote);
    }

    private static bool IsSectionLike(string label) =>
        ContainsAny(label.ToLowerInvariant(),
            "intro", "verse", "chorus", "bridge", "break", "build", "drop", "finale", "outro", "yeah", "hey", "oh");

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => term.Length <= 3
            ? Regex.IsMatch(
                value,
                $@"(?<![a-z0-9]){Regex.Escape(term)}(?![a-z0-9])",
                RegexOptions.CultureInvariant)
            : value.Contains(term, StringComparison.Ordinal));
}