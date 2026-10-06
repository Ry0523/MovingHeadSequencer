using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Domain;

namespace MovingHeadSequencer.Layout;

internal static class MovingHeadLayoutSelector
{
    public static MovingHeadLayout Select(MovingHeadLayout layout, FixtureSelectionSettings selection)
    {
        if (selection.IsEmpty)
        {
            return layout;
        }

        var fixturesByName = layout.Fixtures.ToDictionary(fixture => fixture.Name, StringComparer.Ordinal);
        ValidateNames(selection.IncludeNames.Concat(selection.ExcludeNames), fixturesByName.Keys, "fixture");
        ValidateNames(selection.IncludeGroups, layout.Groups.Select(group => group.Name), "fixture group");

        var hasIncludes = selection.IncludeNames.Count > 0 || selection.IncludeGroups.Count > 0 ||
            selection.IncludeTags.Count > 0;
        var included = hasIncludes
            ? new HashSet<string>(StringComparer.Ordinal)
            : fixturesByName.Keys.ToHashSet(StringComparer.Ordinal);
        included.UnionWith(selection.IncludeNames);
        foreach (var groupName in selection.IncludeGroups)
        {
            included.UnionWith(layout.Groups.Single(group => group.Name.Equals(groupName, StringComparison.Ordinal)).FixtureNames);
        }
        foreach (var tag in selection.IncludeTags)
        {
            var tagged = layout.Fixtures
                .Where(fixture => fixture.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .Select(fixture => fixture.Name)
                .ToArray();
            if (tagged.Length == 0)
            {
                throw new InvalidDataException($"No moving-head fixture has tag '{tag}'.");
            }
            included.UnionWith(tagged);
        }
        included.ExceptWith(selection.ExcludeNames);

        var fixtures = layout.Fixtures
            .Where(fixture => included.Contains(fixture.Name))
            .Select((fixture, index) => fixture with { PositionIndex = index })
            .ToArray();
        if (fixtures.Length == 0)
        {
            throw new InvalidDataException("Fixture selection removed every moving head.");
        }
        var selectedByName = fixtures.ToDictionary(fixture => fixture.Name, StringComparer.Ordinal);

        var controls = layout.Controls.Select(control =>
            {
                var originalNodeIndexes = control.NodeIndexes ?? Enumerable.Range(0, control.FixtureNames.Count).ToArray();
                var selected = control.FixtureNames
                    .Select((name, index) => new { Name = name, NodeIndex = originalNodeIndexes[index] })
                    .Where(item => selectedByName.ContainsKey(item.Name))
                    .ToArray();
                return selected.Length == 0
                    ? null
                    : new MovingHeadControl(
                        control.Name,
                        control.Role,
                        selected.Select(item => item.Name).ToArray(),
                        selected.Select(item => selectedByName[item.Name].PositionIndex).ToArray(),
                        selected.Select(item => item.NodeIndex).ToArray());
            })
            .Where(control => control is not null)
            .Cast<MovingHeadControl>()
            .ToArray();

        var warnings = layout.Warnings
            .Where(warning => !IsCoverageWarning(warning))
            .Where(warning => !layout.Fixtures.Any(fixture => warning.Contains(fixture.Name, StringComparison.Ordinal)) ||
                              fixtures.Any(fixture => warning.Contains(fixture.Name, StringComparison.Ordinal)))
            .ToList();
        AddCoverageWarnings(fixtures, controls, warnings);

        var groups = layout.Groups
            .Select(group => group with
            {
                FixtureNames = group.FixtureNames.Where(included.Contains).ToArray(),
            })
            .Where(group => group.FixtureNames.Count > 0)
            .ToArray();
        var primaryGroupName = groups.FirstOrDefault(group => group.FixtureNames.Count == fixtures.Length)?.Name;
        return layout with
        {
            Fixtures = fixtures,
            Controls = controls,
            Groups = groups,
            PrimaryGroupName = primaryGroupName,
            Warnings = warnings,
        };
    }

    private static void ValidateNames(IEnumerable<string> requested, IEnumerable<string> available, string kind)
    {
        var availableSet = available.ToHashSet(StringComparer.Ordinal);
        var unknown = requested.Where(name => !availableSet.Contains(name)).Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidDataException($"Unknown {kind}: {string.Join(", ", unknown)}.");
        }
    }

    private static bool IsCoverageWarning(string warning) =>
        warning.StartsWith("No ", StringComparison.Ordinal) || warning.StartsWith("Multiple ", StringComparison.Ordinal);

    private static void AddCoverageWarnings(
        IReadOnlyList<MovingHeadFixture> fixtures,
        IReadOnlyList<MovingHeadControl> controls,
        ICollection<string> warnings)
    {
        foreach (var role in Enum.GetValues<ControlRole>())
        {
            var covered = controls.Where(control => control.Role == role).SelectMany(control => control.FixtureNames).ToArray();
            var missing = fixtures.Where(fixture => !covered.Contains(fixture.Name, StringComparer.Ordinal)).Select(fixture => fixture.Name).ToArray();
            var duplicate = covered.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (missing.Length > 0)
            {
                warnings.Add($"No {role} control covers: {string.Join(", ", missing)}.");
            }
            if (duplicate.Length > 0)
            {
                warnings.Add($"Multiple {role} controls cover: {string.Join(", ", duplicate)}.");
            }
        }
    }
}