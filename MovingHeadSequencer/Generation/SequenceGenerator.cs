using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Domain;
using MovingHeadSequencer.Sequences;
using MovingHeadSequencer.Xsq;

namespace MovingHeadSequencer.Generation;

internal sealed class SequenceGenerator(string workspaceRoot, bool force, TextWriter output)
{
    private static readonly string[] LegacyModelNames =
    [
        "*****Moving Heads*****",
        "Movers",
        "Mover1",
        "Mover2",
        "Mover3",
        "Mover4",
        "MH-Dimmers",
        "MH-Pan",
        "MH-Shutters",
        "MH-Tilt",
    ];

    public string Generate(SequencePlan plan, MovingHeadLayout? layout, int headCount)
    {
        var sourcePath = ResolvePath(plan.SourceFileName);
        var outputPath = ResolvePath(plan.OutputFileName);
        if (File.Exists(outputPath) && !force)
        {
            throw new IOException($"Output already exists: {outputPath}. Use --force to replace it.");
        }

        var editor = XsqDocumentEditor.Load(sourcePath);
        if (plan.Kind == SequenceKind.Storm)
        {
            PrepareStorm(editor, layout, headCount, plan.PreserveLegacyDimmer);
        }
        else
        {
            PrepareLilJon(editor, layout, headCount);
        }

        var references = editor.AddEffectDefinitions(plan.Definitions);
        var panTracks = TrackBuilder.Build(plan.PanCues, headCount);
        var tiltTracks = TrackBuilder.Build(plan.TiltCues, headCount);
        var dimmerTracks = TrackBuilder.Build(plan.DimmerCues, headCount);

        if (layout is not null)
        {
            SetLayoutControlTracks(editor, layout, ControlRole.Pan, panTracks, references, plan.Palette, "DMX");
            SetLayoutControlTracks(editor, layout, ControlRole.Tilt, tiltTracks, references, plan.Palette, "DMX");
            SetLayoutControlTracks(editor, layout, ControlRole.Dimmer, dimmerTracks, references, plan.Palette, "On");
            foreach (var control in layout.Controls.Where(control => control.Role == ControlRole.Shutter))
            {
                editor.SetRootEffects(control.Name, plan.ShutterEvents, references, plan.Palette, "On");
            }

            AssertLayoutControlTracks(editor, layout, ControlRole.Pan, plan.Duration);
            AssertLayoutControlTracks(editor, layout, ControlRole.Tilt, plan.Duration);
            AssertLayoutControlTracks(editor, layout, ControlRole.Dimmer, plan.Duration, allowGaps: plan.DimmerCues[0].Start > 0);
        }
        else
        {
            editor.SetControlTrack("MH-Pan", panTracks, references, plan.Palette, headCount);
            editor.SetControlTrack("MH-Tilt", tiltTracks, references, plan.Palette, headCount);
            if (!plan.PreserveLegacyDimmer)
            {
                editor.SetControlTrack("MH-Dimmers", dimmerTracks, references, plan.Palette, headCount, "On");
            }
            if (plan.ShutterEvents.Count > 0)
            {
                editor.SetRootEffects("MH-Shutters", plan.ShutterEvents, references, plan.Palette, "On");
            }

            editor.AssertControlTrack("MH-Pan", plan.Duration, headCount, frameIntervalMs: plan.FrameIntervalMs);
            editor.AssertControlTrack("MH-Tilt", plan.Duration, headCount, frameIntervalMs: plan.FrameIntervalMs);
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
                    $"Expected {headCount} inherited dimmer nodes; found {editor.GetControlNodeCount("MH-Dimmers")}.");
            }
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDirectory);
        var stagingPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            editor.Save(stagingPath);
            GeneratedXsqValidator.Validate(stagingPath, plan, layout, headCount);
            File.Move(stagingPath, outputPath, overwrite: force);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
        output.WriteLine($"Created {outputPath}");
        output.WriteLine("Post-write XSQ validation passed.");
        output.WriteLine($"Added {plan.Definitions.Items.Count} reusable movement and intensity definitions.");
        output.WriteLine($"Pan cues per head: {plan.PanCues.Count}; tilt cues per head: {plan.TiltCues.Count}.");
        if (plan.DimmerCues.Count > 0)
        {
            output.WriteLine(
                $"Dimmer cue groups: {plan.DimmerCues.Count}; shutter windows: {plan.ShutterEvents.Count}.");
        }
        return outputPath;
    }

    private string ResolvePath(string path) =>
        Path.IsPathFullyQualified(path) ? path : Path.Combine(workspaceRoot, path);

    private static void PrepareStorm(
        XsqDocumentEditor editor,
        MovingHeadLayout? layout,
        int headCount,
        bool preserveLegacyDimmer)
    {
        if (layout is not null)
        {
            editor.RemoveModels(LegacyModelNames);
            editor.AddModelScaffolding(GetLayoutModelNames(layout));
            return;
        }

        editor.ExpandLegacyFixtureModels(headCount);
        editor.ResetControlTrack("MH-Shutters");
        if (!preserveLegacyDimmer)
        {
            editor.ResetControlTrack("MH-Dimmers");
        }
    }

    private static void PrepareLilJon(XsqDocumentEditor editor, MovingHeadLayout? layout, int headCount)
    {
        if (layout is not null)
        {
            editor.AddModelScaffolding(GetLayoutModelNames(layout));
            return;
        }

        var fixtureNames = Enumerable.Range(1, headCount).Select(index => $"Mover{index}");
        editor.AddModelScaffolding(
            new[] { "*****Moving Heads*****", "Movers" }
                .Concat(fixtureNames)
                .Concat(["MH-Dimmers", "MH-Pan", "MH-Shutters", "MH-Tilt"]));
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<TrackEvent>> SelectControlTracks(
        IReadOnlyDictionary<int, IReadOnlyList<TrackEvent>> globalTracks,
        MovingHeadControl control)
    {
        var nodeIndexes = control.NodeIndexes ?? Enumerable.Range(0, control.PositionIndexes.Count).ToArray();
        return control.PositionIndexes
            .Select((positionIndex, localIndex) => new { nodeIndex = nodeIndexes[localIndex], events = globalTracks[positionIndex] })
            .ToDictionary(item => item.nodeIndex, item => item.events);
    }

    private static void SetLayoutControlTracks(
        XsqDocumentEditor editor,
        MovingHeadLayout layout,
        ControlRole role,
        IReadOnlyDictionary<int, IReadOnlyList<TrackEvent>> globalTracks,
        IReadOnlyDictionary<string, int> references,
        int palette,
        string effectName)
    {
        foreach (var control in layout.Controls.Where(control => control.Role == role))
        {
            var localTracks = SelectControlTracks(globalTracks, control);
            editor.SetControlTrack(
                control.Name,
                localTracks,
                references,
                palette,
                control.PositionIndexes.Count,
                effectName);
        }
    }

    private static void AssertLayoutControlTracks(
        XsqDocumentEditor editor,
        MovingHeadLayout layout,
        ControlRole role,
        int expectedEnd,
        bool allowGaps = false)
    {
        foreach (var control in layout.Controls.Where(control => control.Role == role))
        {
            editor.AssertControlTrack(
                control.Name,
                expectedEnd,
                control.PositionIndexes.Count,
                allowGaps);
        }
    }

    private static IEnumerable<string> GetLayoutModelNames(MovingHeadLayout layout)
    {
        if (!string.IsNullOrWhiteSpace(layout.PrimaryGroupName))
        {
            yield return layout.PrimaryGroupName;
        }
        foreach (var name in layout.Fixtures.Select(fixture => fixture.Name)
                     .Concat(layout.Controls.Select(control => control.Name))
                     .Distinct(StringComparer.Ordinal))
        {
            yield return name;
        }
    }
}