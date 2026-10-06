namespace MovingHeadSequencer.Audit;

internal enum ReferenceTier
{
    Core,
    Supplemental,
    Target,
}

internal sealed record ReferenceSource(ReferenceTier Tier, string FileName);

internal static class ReferenceSourceCatalog
{
    public static IReadOnlyList<ReferenceSource> Sources { get; } =
    [
        new(ReferenceTier.Core, "A-Christmas-Storm(old_layout).xsq"),
        new(ReferenceTier.Core, "Where are you Christmas - The Pretty Reckless HD.xsq"),
        new(ReferenceTier.Core, "Bloody Mary HD.xsq"),
        new(ReferenceTier.Core, "Aronchupa - Rave In The Grave HD Layout.xsq"),
        new(ReferenceTier.Core, "I Gotta Feeling HD Layout .xsq"),
        new(ReferenceTier.Core, "Dominick-the-Donkey-Update.xsq"),
        new(ReferenceTier.Supplemental, "Firework HD Layout 23 update.xsq"),
        new(ReferenceTier.Supplemental, "Look What You Made Me Do HD MHPan Update.xsq"),
        new(ReferenceTier.Supplemental, "Motionless in White - Werewolf HD Layout.xsq"),
        new(ReferenceTier.Supplemental, "Opalite - Taylor Swift HD Version.xsq"),
        new(ReferenceTier.Supplemental, "KPop Demon Hunters - Your Idol HD Layout.xsq"),
        new(ReferenceTier.Supplemental, "Demon Hunter Medley Store.xsq"),
        new(ReferenceTier.Supplemental, "Magic Updated 11.xsq"),
        new(ReferenceTier.Supplemental, "Uptown Funk (Radio Edit) Updated 11.xsq"),
        new(ReferenceTier.Supplemental, "XATW Believer-2-1.xsq"),
        new(ReferenceTier.Supplemental, "XATW Sounding Joy-2-1.xsq"),
        new(ReferenceTier.Supplemental, "Halloween Horror Lights 2024 FINAL.xsq"),
        new(ReferenceTier.Supplemental, "Halloween Horror Lights 2025 FINAL.xsq"),
    ];
}