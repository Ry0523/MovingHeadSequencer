using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using MovingHeadSequencer.Domain;

namespace MovingHeadSequencer.Layout;

internal static partial class MovingHeadLayoutResolver
{
    public static MovingHeadLayout Resolve(RgbEffectsDocument rgbEffects, string? groupName)
    {
        var allFixtureElements = SelectElements(rgbEffects.Document, "//model")
            .Where(element => element.GetAttribute("DisplayAs").StartsWith("DmxMovingHead", StringComparison.Ordinal))
            .ToArray();

        XmlElement? selectedGroup = null;
        var fixtureElements = allFixtureElements;
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            var matchingGroups = SelectElements(rgbEffects.Document, "//modelGroup")
                .Where(element => element.GetAttribute("name").Equals(groupName, StringComparison.Ordinal))
                .ToArray();
            if (matchingGroups.Length != 1)
            {
                throw new InvalidDataException(
                    $"Expected one moving-head group named '{groupName}'; found {matchingGroups.Length}.");
            }

            selectedGroup = matchingGroups[0];
            var selectedNames = SplitModelNames(selectedGroup.GetAttribute("models"));
            var fixtureNameSet = allFixtureElements.Select(element => element.GetAttribute("name")).ToHashSet(StringComparer.Ordinal);
            var invalidNames = selectedNames.Where(name => !fixtureNameSet.Contains(name)).ToArray();
            if (invalidNames.Length > 0)
            {
                throw new InvalidDataException(
                    $"Group '{groupName}' contains non-moving-head or missing models: {string.Join(", ", invalidNames)}.");
            }

            var selectedNameSet = selectedNames.ToHashSet(StringComparer.Ordinal);
            fixtureElements = allFixtureElements
                .Where(element => selectedNameSet.Contains(element.GetAttribute("name")))
                .ToArray();
        }

        if (fixtureElements.Length == 0)
        {
            throw new InvalidDataException($"No DmxMovingHead models were found in '{rgbEffects.Source}'.");
        }

        var orderedFixtures = fixtureElements
            .Select(element => new
            {
                Element = element,
                Name = element.GetAttribute("name"),
                PositionX = TryParseDouble(element.GetAttribute("WorldPosX")),
                PanChannel = GetModelChannel(element, ControlRole.Pan),
                TiltChannel = GetModelChannel(element, ControlRole.Tilt),
                DimmerChannel = GetModelChannel(element, ControlRole.Dimmer),
                ShutterChannel = GetModelChannel(element, ControlRole.Shutter),
            })
            .OrderBy(fixture => double.IsNaN(fixture.PositionX) ? double.PositiveInfinity : fixture.PositionX)
            .ThenBy(fixture => fixture.Name, StringComparer.Ordinal)
            .ToArray();

        var fixtures = orderedFixtures
            .Select((fixture, index) => new MovingHeadFixture(
                fixture.Name,
                fixture.PositionX,
                fixture.PanChannel,
                fixture.TiltChannel,
                fixture.DimmerChannel,
                fixture.ShutterChannel,
                fixture.Element,
                index,
                GetTags(fixture.Element)))
            .ToArray();
        var fixtureByName = fixtures.ToDictionary(fixture => fixture.Name, StringComparer.Ordinal);
        var warnings = new List<string>();
        var controls = DiscoverControls(rgbEffects.Document, fixtureByName, warnings);

        foreach (var role in Enum.GetValues<ControlRole>())
        {
            var coveredNames = controls
                .Where(control => control.Role == role)
                .SelectMany(control => control.FixtureNames)
                .ToArray();
            var missingNames = fixtures
                .Where(fixture => !coveredNames.Contains(fixture.Name, StringComparer.Ordinal))
                .Select(fixture => fixture.Name)
                .ToArray();
            var duplicateNames = coveredNames
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (missingNames.Length > 0)
            {
                warnings.Add($"No {role} control covers: {string.Join(", ", missingNames)}.");
            }
            if (duplicateNames.Length > 0)
            {
                warnings.Add($"Multiple {role} controls cover: {string.Join(", ", duplicateNames)}.");
            }
        }

        var fixtureNames = fixtures.Select(fixture => fixture.Name).ToHashSet(StringComparer.Ordinal);
        var groups = SelectElements(rgbEffects.Document, "//modelGroup")
            .Select(element => new MovingHeadGroup(
                element.GetAttribute("name"),
                SplitModelNames(element.GetAttribute("models"))))
            .Where(group => group.FixtureNames.Count > 0 && group.FixtureNames.All(fixtureNames.Contains))
            .ToArray();

        var primaryGroupName = selectedGroup?.GetAttribute("name") ??
            groups.FirstOrDefault(group => group.FixtureNames.Count == fixtures.Length)?.Name;

        return new MovingHeadLayout(
            rgbEffects.Source,
            fixtures,
            controls,
            groups,
            primaryGroupName,
            warnings);
    }

    public static void ValidateForGeneration(MovingHeadLayout layout, int headCount, bool allowWarnings)
    {
        if (layout.Fixtures.Count != headCount)
        {
            throw new InvalidDataException(
                $"RGB-effects layout contains {layout.Fixtures.Count} moving heads, but --head-count is {headCount}. " +
                "Use the matching count or a different layout file.");
        }

        foreach (var role in Enum.GetValues<ControlRole>())
        {
            if (!layout.Controls.Any(control => control.Role == role))
            {
                throw new InvalidDataException($"RGB-effects layout has no discovered {role} control model.");
            }
        }

        if (layout.Warnings.Count > 0 && !allowWarnings)
        {
            throw new InvalidDataException(
                "RGB-effects layout has wiring warnings. Inspect and fix the layout, or explicitly use " +
                $"--allow-layout-warnings:{Environment.NewLine}{string.Join(Environment.NewLine, layout.Warnings)}");
        }
    }

    private static MovingHeadControl[] DiscoverControls(
        XmlDocument document,
        IReadOnlyDictionary<string, MovingHeadFixture> fixtureByName,
        ICollection<string> warnings)
    {
        var controls = new List<MovingHeadControl>();
        foreach (var model in SelectElements(document, "//model[@DisplayAs='Single Line' and @Advanced='1']"))
        {
            var stringAttributes = model.Attributes.Cast<XmlAttribute>()
                .Where(attribute => StringAttributeRegex().IsMatch(attribute.Name))
                .OrderBy(attribute => int.Parse(attribute.Name.AsSpan(6), CultureInfo.InvariantCulture))
                .ToArray();
            if (stringAttributes.Length == 0)
            {
                continue;
            }

            var declaredCount = int.TryParse(model.GetAttribute("parm1"), out var parsedCount)
                ? parsedCount
                : stringAttributes.Length;
            var references = stringAttributes
                .Take(declaredCount)
                .Select(attribute => ParseChannelReference(attribute.Value))
                .ToArray();
            if (references.Length == 0 || references.Any(reference =>
                    reference is null || !fixtureByName.ContainsKey(reference.Value.ModelName)))
            {
                continue;
            }

            var resolvedReferences = references.Select(reference => reference!.Value).ToArray();
            var firstFixture = fixtureByName[resolvedReferences[0].ModelName];
            var role = Enum.GetValues<ControlRole>()
                .Cast<ControlRole?>()
                .FirstOrDefault(candidate =>
                    candidate is not null && GetChannel(firstFixture, candidate.Value) == resolvedReferences[0].Channel);
            if (role is null)
            {
                continue;
            }

            foreach (var reference in resolvedReferences)
            {
                var expectedChannel = GetChannel(fixtureByName[reference.ModelName], role.Value);
                if (reference.Channel != expectedChannel)
                {
                    warnings.Add(
                        $"Control '{model.GetAttribute("name")}' maps '{reference.ModelName}' channel {reference.Channel} " +
                        $"as {role}; fixture metadata declares channel {expectedChannel}.");
                }
            }

            controls.Add(new MovingHeadControl(
                model.GetAttribute("name"),
                role.Value,
                resolvedReferences.Select(reference => reference.ModelName).ToArray(),
                resolvedReferences.Select(reference => fixtureByName[reference.ModelName].PositionIndex).ToArray(),
                Enumerable.Range(0, resolvedReferences.Length).ToArray()));
        }

        return controls.ToArray();
    }

    private static int GetModelChannel(XmlElement model, ControlRole role)
    {
        if (role is ControlRole.Pan or ControlRole.Tilt)
        {
            var motor = model.SelectSingleNode($"{role}Motor") as XmlElement;
            if (motor is not null && int.TryParse(motor.GetAttribute("ChannelCoarse"), out var motorChannel))
            {
                return motorChannel;
            }
        }

        var subModel = model.SelectNodes("subModel")
            ?.Cast<XmlElement>()
            .FirstOrDefault(element => element.GetAttribute("name").Equals(role.ToString(), StringComparison.OrdinalIgnoreCase));
        if (subModel is not null && int.TryParse(subModel.GetAttribute("line0"), out var subModelChannel))
        {
            return subModelChannel;
        }

        var attributeNames = role switch
        {
            ControlRole.Dimmer => new[] { "DmxDimmerChannel", "MhDimmerChannel" },
            ControlRole.Shutter => new[] { "DmxShutterChannel" },
            _ => [],
        };
        foreach (var attributeName in attributeNames)
        {
            if (int.TryParse(model.GetAttribute(attributeName), out var channel) && channel > 0)
            {
                return channel;
            }
        }

        var nodeNames = model.GetAttribute("NodeNames").Split(',');
        for (var index = 0; index < nodeNames.Length; index++)
        {
            if (nodeNames[index].Trim().Equals(role.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        throw new InvalidDataException(
            $"Could not determine the {role} channel for moving-head model '{model.GetAttribute("name")}'.");
    }

    private static int GetChannel(MovingHeadFixture fixture, ControlRole role) => role switch
    {
        ControlRole.Pan => fixture.PanChannel,
        ControlRole.Tilt => fixture.TiltChannel,
        ControlRole.Dimmer => fixture.DimmerChannel,
        ControlRole.Shutter => fixture.ShutterChannel,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static (string ModelName, int Channel)? ParseChannelReference(string value)
    {
        var match = ChannelReferenceRegex().Match(value);
        return match.Success
            ? (match.Groups["Model"].Value, int.Parse(match.Groups["Channel"].Value, CultureInfo.InvariantCulture))
            : null;
    }

    private static string[] SplitModelNames(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] GetTags(XmlElement element) =>
        element.Attributes.Cast<XmlAttribute>()
            .Where(attribute => attribute.Name.Equals("tag", StringComparison.OrdinalIgnoreCase) ||
                                attribute.Name.Equals("tags", StringComparison.OrdinalIgnoreCase))
            .SelectMany(attribute => attribute.Value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<XmlElement> SelectElements(XmlDocument document, string xpath) =>
        document.SelectNodes(xpath)?.Cast<XmlElement>() ?? [];

    private static double TryParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : double.NaN;

    [GeneratedRegex("^String\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex StringAttributeRegex();

    [GeneratedRegex("^@(?<Model>.+):(?<Channel>\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelReferenceRegex();
}