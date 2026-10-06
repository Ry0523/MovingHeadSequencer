using System.Globalization;
using System.Xml;
using MovingHeadSequencer.Domain;
using MovingHeadSequencer.Sequences;
using MovingHeadSequencer.Xsq;

namespace MovingHeadSequencer.Generation;

internal static class GeneratedXsqValidator
{
    public static void Validate(
        string path,
        SequencePlan plan,
        MovingHeadLayout? layout,
        int headCount)
    {
        try
        {
            var editor = XsqDocumentEditor.Load(path);
            ValidateCoreStructure(editor.Document);
            ValidateModelReferences(editor.Document);
            ValidateGeneratedTracks(editor, plan, layout, headCount);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or XmlException or FormatException or OverflowException)
        {
            throw new InvalidDataException(
                $"Generated XSQ failed post-write validation: {exception.Message}",
                exception);
        }
    }

    private static void ValidateCoreStructure(XmlDocument document)
    {
        if (document.DocumentElement?.Name != "xsequence")
        {
            throw new InvalidDataException("The document root is not xsequence.");
        }

        foreach (var (path, name) in new[]
        {
            ("/xsequence/head", "head"),
            ("/xsequence/EffectDB", "EffectDB"),
            ("/xsequence/ColorPalettes", "ColorPalettes"),
            ("/xsequence/DisplayElements", "DisplayElements"),
            ("/xsequence/ElementEffects", "ElementEffects"),
        })
        {
            if (document.SelectSingleNode(path) is null)
            {
                throw new InvalidDataException($"Required xLights section '{name}' is missing.");
            }
        }

        var durationText = document.SelectSingleNode("/xsequence/head/sequenceDuration")?.InnerText;
        if (!double.TryParse(
                durationText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var durationSeconds) ||
            durationSeconds <= 0)
        {
            throw new InvalidDataException("sequenceDuration must be a positive number.");
        }
    }

    private static void ValidateModelReferences(XmlDocument document)
    {
        var effectCount = document.SelectNodes("/xsequence/EffectDB/Effect")?.Count ?? 0;
        var paletteCount = document.SelectNodes("/xsequence/ColorPalettes/ColorPalette")?.Count ?? 0;
        var effects = document.SelectNodes(
                "/xsequence/ElementEffects/Element[@type='model']//Effect")
            ?.Cast<XmlElement>() ?? [];

        foreach (var effect in effects)
        {
            var reference = ParseIndex(effect, "ref");
            if (reference >= effectCount)
            {
                throw new InvalidDataException(
                    $"Model effect reference {reference} exceeds EffectDB index {effectCount - 1}.");
            }

            var palette = ParseIndex(effect, "palette");
            if (palette >= paletteCount)
            {
                throw new InvalidDataException(
                    $"Model effect palette {palette} exceeds ColorPalettes index {paletteCount - 1}.");
            }
        }
    }

    private static void ValidateGeneratedTracks(
        XsqDocumentEditor editor,
        SequencePlan plan,
        MovingHeadLayout? layout,
        int headCount)
    {
        if (layout is not null)
        {
            AssertLayoutTracks(editor, layout, ControlRole.Pan, plan);
            AssertLayoutTracks(editor, layout, ControlRole.Tilt, plan);
            AssertLayoutTracks(
                editor,
                layout,
                ControlRole.Dimmer,
                plan,
                allowGaps: plan.DimmerCues[0].Start > 0);
            if (plan.ShutterEvents.Count > 0)
            {
                foreach (var control in layout.Controls.Where(control => control.Role == ControlRole.Shutter))
                {
                    editor.AssertRootEffects(control.Name, plan.ShutterEvents, plan.FrameIntervalMs);
                }
            }
            return;
        }

        editor.AssertControlTrack(
            "MH-Pan",
            plan.Duration,
            headCount,
            frameIntervalMs: plan.FrameIntervalMs);
        editor.AssertControlTrack(
            "MH-Tilt",
            plan.Duration,
            headCount,
            frameIntervalMs: plan.FrameIntervalMs);
        if (!plan.PreserveLegacyDimmer)
        {
            editor.AssertControlTrack(
                "MH-Dimmers",
                plan.Duration,
                headCount,
                allowGaps: plan.DimmerCues[0].Start > 0,
                frameIntervalMs: plan.FrameIntervalMs);
        }
        else if (editor.GetControlNodeCount("MH-Dimmers") != headCount)
        {
            throw new InvalidDataException(
                $"Expected {headCount} inherited dimmer nodes after persistence; " +
                $"found {editor.GetControlNodeCount("MH-Dimmers")}.");
        }

        if (plan.ShutterEvents.Count > 0)
        {
            editor.AssertRootEffects("MH-Shutters", plan.ShutterEvents, plan.FrameIntervalMs);
        }
    }

    private static void AssertLayoutTracks(
        XsqDocumentEditor editor,
        MovingHeadLayout layout,
        ControlRole role,
        SequencePlan plan,
        bool allowGaps = false)
    {
        foreach (var control in layout.Controls.Where(control => control.Role == role))
        {
            editor.AssertControlTrack(
                control.Name,
                plan.Duration,
                control.PositionIndexes.Count,
                allowGaps,
                plan.FrameIntervalMs);
        }
    }

    private static int ParseIndex(XmlElement effect, string attribute)
    {
        if (!int.TryParse(
                effect.GetAttribute(attribute),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value) ||
            value < 0)
        {
            throw new InvalidDataException(
                $"Model effect has an invalid {attribute} value '{effect.GetAttribute(attribute)}'.");
        }
        return value;
    }
}