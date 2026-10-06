namespace MovingHeadSequencer.Configuration;

internal static class HeadCountPolicy
{
    public const int Minimum = 2;
    public const int Maximum = 12;

    public static bool IsSupported(int count) => count is >= Minimum and <= Maximum;

    public static string RangeText => $"{Minimum} through {Maximum}";
}