using System.Globalization;
using MovingHeadSequencer.Domain;

namespace MovingHeadSequencer.Layout;

internal static class LayoutReporter
{
    public static void Write(MovingHeadLayout layout, TextWriter output)
    {
        output.WriteLine($"RGB effects: {layout.Source}");
        output.WriteLine($"Moving heads: {layout.Fixtures.Count}");
        foreach (var fixture in layout.Fixtures)
        {
            output.WriteLine(
                $"  [{fixture.PositionIndex}] {fixture.Name} " +
                $"X={fixture.PositionX.ToString(CultureInfo.InvariantCulture)} channels: " +
                $"dimmer={fixture.DimmerChannel}, shutter={fixture.ShutterChannel}, " +
                $"pan={fixture.PanChannel}, tilt={fixture.TiltChannel}");
        }

        if (!string.IsNullOrWhiteSpace(layout.PrimaryGroupName))
        {
            output.WriteLine($"Primary group: {layout.PrimaryGroupName}");
        }

        foreach (var control in layout.Controls)
        {
            output.WriteLine($"  {control.Role}: {control.Name} -> {string.Join(", ", control.FixtureNames)}");
        }

        foreach (var warning in layout.Warnings)
        {
            output.WriteLine($"WARNING: {warning}");
        }
    }
}