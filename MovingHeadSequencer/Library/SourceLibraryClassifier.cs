using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace MovingHeadSequencer.Library;

internal enum SourceClassification
{
    ActiveReference,
    InactivePlaceholder,
    GeneratedOutput,
    Duplicate,
    NoMovingHeadModels,
    Invalid,
}

internal sealed record SourceClassificationResult(
    string FileName,
    SourceClassification Classification,
    int MovingHeadModelCount,
    int MovingHeadEffectCount,
    string? DuplicateOf,
    string? Error);

internal static partial class SourceLibraryClassifier
{
    public static IReadOnlyList<SourceClassificationResult> Classify(string root)
    {
        var candidates = Directory.EnumerateFiles(root, "*.xsq", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Inspect)
            .ToArray();
        var duplicatePrimary = candidates
            .Where(candidate => candidate.Result.Classification == SourceClassification.ActiveReference)
            .GroupBy(candidate => candidate.Signature, StringComparer.Ordinal)
            .Where(group => group.Key is not null && group.Count() > 1)
            .ToDictionary(
                group => group.Key!,
                group => group.OrderByDescending(item => item.Result.FileName.Contains("Updated", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(item => item.Result.FileName, StringComparer.OrdinalIgnoreCase)
                    .First().Result.FileName,
                StringComparer.Ordinal);

        return candidates.Select(candidate =>
        {
            if (candidate.Signature is null || !duplicatePrimary.TryGetValue(candidate.Signature, out var primary) ||
                candidate.Result.FileName.Equals(primary, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Result;
            }
            return candidate.Result with
            {
                Classification = SourceClassification.Duplicate,
                DuplicateOf = primary,
            };
        }).ToArray();
    }

    public static void WriteReport(
        IReadOnlyList<SourceClassificationResult> results,
        TextWriter output,
        string format)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                var options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonStringEnumConverter());
                output.Write(JsonSerializer.Serialize(results, options));
                break;
            case "csv":
                output.WriteLine("sequence,classification,movingHeadModels,movingHeadEffects,duplicateOf,error");
                foreach (var result in results)
                {
                    output.WriteLine(string.Join(',',
                        Csv(result.FileName), result.Classification, result.MovingHeadModelCount,
                        result.MovingHeadEffectCount, Csv(result.DuplicateOf ?? string.Empty), Csv(result.Error ?? string.Empty)));
                }
                break;
            case "table":
                output.WriteLine("Classification\tModels\tEffects\tSequence\tDuplicateOf/Error");
                foreach (var result in results)
                {
                    output.WriteLine(string.Join('\t', result.Classification, result.MovingHeadModelCount,
                        result.MovingHeadEffectCount, result.FileName, result.DuplicateOf ?? result.Error ?? string.Empty));
                }
                break;
            default:
                throw new InvalidDataException($"Unsupported classification format '{format}'.");
        }
    }

    private static InspectedSource Inspect(string path)
    {
        var fileName = Path.GetFileName(path);
        if (GeneratedOutputRegex().IsMatch(Path.GetFileNameWithoutExtension(path)))
        {
            return new InspectedSource(
                new SourceClassificationResult(fileName, SourceClassification.GeneratedOutput, 0, 0, null, null),
                null);
        }

        try
        {
            var document = new XmlDocument();
            document.Load(path);
            var models = document.SelectNodes("/xsequence/ElementEffects/Element[@type='model']")
                ?.Cast<XmlElement>()
                .Where(element => MovingHeadNameRegex().IsMatch(element.GetAttribute("name")))
                .ToArray() ?? [];
            if (models.Length == 0)
            {
                return new InspectedSource(
                    new SourceClassificationResult(fileName, SourceClassification.NoMovingHeadModels, 0, 0, null, null),
                    null);
            }
            var effects = models.SelectMany(model => model.SelectNodes(".//Effect")?.Cast<XmlElement>() ?? []).ToArray();
            if (effects.Length == 0)
            {
                return new InspectedSource(
                    new SourceClassificationResult(fileName, SourceClassification.InactivePlaceholder, models.Length, 0, null, null),
                    null);
            }

            return new InspectedSource(
                new SourceClassificationResult(fileName, SourceClassification.ActiveReference, models.Length, effects.Length, null, null),
                CreateSignature(document, models));
        }
        catch (Exception exception) when (exception is IOException or XmlException)
        {
            return new InspectedSource(
                new SourceClassificationResult(fileName, SourceClassification.Invalid, 0, 0, null, exception.Message),
                null);
        }
    }

    private static string CreateSignature(XmlDocument document, IEnumerable<XmlElement> models)
    {
        var effectDb = document.SelectNodes("/xsequence/EffectDB/Effect")?.Cast<XmlElement>().ToArray() ?? [];
        var builder = new StringBuilder();
        foreach (var model in models.OrderBy(model => model.GetAttribute("name"), StringComparer.Ordinal))
        {
            builder.Append(model.GetAttribute("name")).Append('|');
            foreach (var effect in model.SelectNodes(".//Effect")?.Cast<XmlElement>() ?? [])
            {
                var settings = int.TryParse(effect.GetAttribute("ref"), out var reference) &&
                               reference >= 0 && reference < effectDb.Length
                    ? effectDb[reference].InnerText
                    : string.Empty;
                builder.Append(effect.GetAttribute("name")).Append(':')
                    .Append(effect.GetAttribute("startTime")).Append('-')
                    .Append(effect.GetAttribute("endTime")).Append(':')
                    .Append(settings).Append(';');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private sealed record InspectedSource(SourceClassificationResult Result, string? Signature);

    [GeneratedRegex("-\\d+MH$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedOutputRegex();

    [GeneratedRegex("(moving head|(^|[^a-z])mh([^a-z]|$)|mover|dmxmovinghead|dmx head|lempa)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovingHeadNameRegex();
}