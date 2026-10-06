using System.Xml;

namespace MovingHeadSequencer.Domain;

internal enum ControlRole
{
    Pan,
    Tilt,
    Dimmer,
    Shutter,
}

internal sealed record MovingHeadFixture(
    string Name,
    double PositionX,
    int PanChannel,
    int TiltChannel,
    int DimmerChannel,
    int ShutterChannel,
    XmlElement Element,
    int PositionIndex,
    IReadOnlyList<string> Tags);

internal sealed record MovingHeadControl(
    string Name,
    ControlRole Role,
    IReadOnlyList<string> FixtureNames,
    IReadOnlyList<int> PositionIndexes,
    IReadOnlyList<int>? NodeIndexes = null);

internal sealed record MovingHeadGroup(string Name, IReadOnlyList<string> FixtureNames);

internal sealed record MovingHeadLayout(
    string Source,
    IReadOnlyList<MovingHeadFixture> Fixtures,
    IReadOnlyList<MovingHeadControl> Controls,
    IReadOnlyList<MovingHeadGroup> Groups,
    string? PrimaryGroupName,
    IReadOnlyList<string> Warnings);