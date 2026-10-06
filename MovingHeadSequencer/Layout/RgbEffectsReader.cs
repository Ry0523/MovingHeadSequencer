using System.IO.Compression;
using System.Xml;

namespace MovingHeadSequencer.Layout;

internal sealed record RgbEffectsDocument(XmlDocument Document, string Source);

internal static class RgbEffectsReader
{
    public static RgbEffectsDocument Read(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"RGB-effects input was not found: {resolvedPath}", resolvedPath);
        }

        var document = new XmlDocument { PreserveWhitespace = true };
        var extension = Path.GetExtension(resolvedPath);
        string source;

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xsqz", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(resolvedPath);
            var matches = archive.Entries
                .Where(entry => entry.Name.Equals("xlights_rgbeffects.xml", StringComparison.OrdinalIgnoreCase))
                .Select(entry => new { Entry = entry, Depth = GetDepth(entry.FullName) })
                .ToArray();

            if (matches.Length == 0)
            {
                throw new InvalidDataException($"No xlights_rgbeffects.xml was found in '{resolvedPath}'.");
            }

            var minimumDepth = matches.Min(match => match.Depth);
            var highestMatches = matches.Where(match => match.Depth == minimumDepth).ToArray();
            if (highestMatches.Length != 1)
            {
                var names = string.Join(", ", highestMatches.Select(match => match.Entry.FullName));
                throw new InvalidDataException(
                    $"Multiple xlights_rgbeffects.xml files exist at the highest archive level in '{resolvedPath}': {names}.");
            }

            var entry = highestMatches[0].Entry;
            using var stream = entry.Open();
            document.Load(stream);
            source = $"{resolvedPath}::{entry.FullName}";
        }
        else
        {
            document.Load(resolvedPath);
            source = resolvedPath;
        }

        if (document.DocumentElement?.Name != "xrgb")
        {
            throw new InvalidDataException($"'{source}' is not an xLights RGB-effects document.");
        }

        return new RgbEffectsDocument(document, source);
    }

    private static int GetDepth(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Length - 1;
}