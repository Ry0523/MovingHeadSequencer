# MovingHeadSequencer

Cross-platform .NET 8 tooling for generating, validating, auditing, and managing moving-head choreography in xLights `.xsq` sequences.

## Local Visual Editor

`MovingHeadSequencer.Web` is the standalone visual frontend. It runs entirely on the local computer, binds to `127.0.0.1`, opens the default browser, and does not require xLights or an internet connection.

```powershell
dotnet run --project tools/MovingHeadSequencer.Web -- --workspace-root .
```

The editor provides:

- fixed front-camera Canvas preview with any whole-number fixture count from 2 through 12
- workspace-scanned sequence selector with duplicate-title disambiguation
- floor-mounted/upward preview by default, with a preview-only truss/downward toggle
- locally persisted per-fixture Canvas yaw around the vertical axis in 90° increments
- dimmer- and shutter-aware beam rendering
- timeline playback, scrubbing, and selected-cue looping
- automatic beat-source discovery from XSQ timing tracks
- BPM/confidence display, beat/downbeat grid, and phrase/section labels
- cue-boundary snapping to 25 ms, beats, or downbeats/bars
- local audio relinking with synchronized playback and a min/max/RMS waveform
- cue boundary editing, split, delete, and naming
- all/odd/even/individual fixture selection
- pan, tilt, dimmer, and reusable pattern controls
- per-cue rhythmic movement choices with an explicit Off state
- calibrated motion energy, smooth/punch/double shapes, and fixture phase offsets
- full-output source-inspired flash, rise/hold, textured, fade, and pulse dimmer curves
- automatic ranked regeneration choices with confidence, evidence, Apply, and Undo
- cue-only inspector with separate preview setup, generation dialog, and validation drawer
- runtime workspace folder switching with native browse, validation, and persisted preference
- verified original-file backup, atomic replacement, and reversible restore workflow
- hidden per-sequence/head-count project autosave with strict versioned `.mhproj` import/export
- live safety metrics and validation
- direct generation of a local `.xsq` output

Use `--no-browser` when launching under automation, or `--urls http://127.0.0.1:<port>` to select another local port. The preview intentionally models the generated pan/tilt/dimmer/shutter behavior as a narrow neutral-white beam. It does not program or render color, gobo, prism, focus, or xLights' fixture mesh.

The editor scans top-level `.xsq` files when it starts and excludes invalid or generated `-2MH` through `-12MH` outputs. Storm and Lil Jon use their specialized definitions. Files with aggregate `MH Pan/Tilt/Dimmers/Shutters` controls import DMX holds, ramps, bounces, saw-tooth cycles, per-node On/fades, and shutter windows. Other valid XSQs open with their source timing/media metadata and a parked, dark mover layer. Native `Moving Head` paths without aggregate controls are not yet reverse-compiled.

The editor prefers timing metadata already authored in the XSQ. Numbered tracks such as `Beats` provide downbeats and bar numbering; BPM-labeled tracks are offered as alternate grids. Layer 0 of multi-layer timing tracks supplies phrase labels, while word and phoneme layers remain preserved in the timing model for future detailed views.

When cue sections are subdivided by dimmer or shutter changes, pan/tilt curves are sliced at the same progress point rather than restarted. This prevents short subdivisions from appearing unnaturally fast and prevents the following subdivision from snapping backward. Validation reports any remaining pan/tilt boundary jump introduced by a manual edit. The Floor/Truss switch affects preview orientation only; it does not invert generated DMX values.

The Rhythm menu offers Off; a low-position beat lift; half-, one-, or two-beat full-bank tilt; syncopated tilt; one-by-one, mirrored-pair, odd/even, or center/outer tilt chase; two-beat pan sweep; and four-beat combined pan/tilt orbit. Rhythms use the selected XSQ timing lane and compile as full cycles across adjacent cue sections. Energy scales travel within the calibrated fixture profile; Shape chooses smooth, punch, or double motion; Phase offsets fixtures together, left-to-right, center-out, or by alternating pairs. Automatic lit cues use an All100 base and derive intensity variation from full-range fades and source-inspired Flash, Rise and Hold, Pulse, and Textured Pulse curves. Non-rhythmic pose transitions complete within roughly four beats and hold their destination. Off removes the rhythmic overlay and leaves the normal pan/tilt pose controls active.

One-by-one and mirrored-pair chase cycles isolate intensity as well as movement: inactive fixtures use `D0`, and each beat transfers the cue's base dimmer level to the active fixture or pair. Cue `heads` remains the eligible fixture scope for the complete run.

The source sequence's `<mediaFile>` path may refer to another computer. Click the audio status in the timeline to choose the matching local media file. Audio is decoded by the browser and never uploaded; waveform peak/RMS reduction runs in a local Web Worker. Relinking does not modify the source XSQ.

## Build And Verify

From the sequence directory:

```powershell
dotnet build tools/MovingHeadSequencer.sln
dotnet run --project tools/MovingHeadSequencer.Tests --no-build
```

The verification executable is dependency-free and runs offline. It covers 2–12-head geometry, per-fixture profiles, cue sheets, manifests, layout discovery, fixture filtering, safety validation, activity analytics, source classification, and suggestion rules.

## Compatible Defaults

Existing commands still work. With no configuration file, the application uses four heads and the original calibrated ranges:

```powershell
dotnet run --project tools/MovingHeadSequencer -- --target All --force
dotnet run --project tools/MovingHeadSequencer -- --sequence-path "A-Christmas-Storm(old_layout).xsq" --force
```

A generated file is written beside its source as `<source-name>-<count>MH.xsq`. Song selection still comes from `<head><song>`. The PowerShell compatibility launcher remains available.

Storm is a deliberate legacy special case. Its existing `Mover1` through `Mover4` fixture programming is preserved because it contains useful but incomplete choreography; generation supplements it with aggregate pan/tilt controls and validated shutter gates. Layout-driven generation still removes those legacy model rows when targeting a different discovered rig, where retaining them would address the wrong fixtures.

## Generation Modes

Choose exactly one:

- `--target All`: bundled Storm and Lil Jon definitions
- `--sequence-path <file.xsq>`: one XSQ; known songs use specialized choreography and other files start parked/dark
- `--manifest <file.json>`: repeatable multi-sequence batch

Example:

```powershell
dotnet run --project tools/MovingHeadSequencer -- `
  --manifest tools/MovingHeadSequencer/examples/batch-manifest.example.json `
  --dry-run `
  --validation-report generated/validation.json
```

## Configuration

Pass `--config <path>`, or place `moving-head-sequencer.json` in the workspace root for automatic loading. See `examples/moving-head-sequencer.example.json`.

Precedence is deterministic:

1. Built-in backward-compatible defaults
2. Project JSON
3. Project `sequenceOverrides` entry (matched by path or filename, case-insensitive)
4. Batch-manifest entry
5. Explicit CLI option

Project-config resource paths are relative to `--workspace-root`. Manifest resource paths are relative to the manifest file.

The typed schema configures:

- head count and layout warning policy
- layout file and moving-head group
- fixture selection
- global and per-fixture motion profiles
- validation limits and warning policy
- audit thresholds and default output format
- external cue-sheet path
- per-sequence overrides

Unknown JSON properties and invalid values fail immediately.

## Fixture Profiles

Profiles define safe 0-255 pan/tilt ranges, park positions, working envelopes, and inversion. The global axes apply to the rig; exact entries under `fixtureProfile.fixtures` override individual fixtures after layout selection.

```json
{
  "fixtureProfile": {
    "name": "Roof rig",
    "pan": {
      "minimum": 38,
      "maximum": 212,
      "park": 170,
      "outer": 130,
      "center": 210,
      "inverted": false
    },
    "tilt": {
      "minimum": 38,
      "maximum": 123,
      "park": 50,
      "outer": 85,
      "center": 100,
      "inverted": false
    },
    "fixtures": {
      "Mover1": {
        "pan": {
          "minimum": 38,
          "maximum": 212,
          "park": 170,
          "outer": 130,
          "center": 210,
          "inverted": true
        }
      }
    }
  }
}
```

The same semantic fan, cross, park, rise, and bounce patterns are recalculated through each selected fixture's profile. Unsafe values are rejected before XML is written.

## Layout Discovery And Selection

`--rgb-effects-path` accepts `xlights_rgbeffects.xml`, ZIP, or XSQZ. Discovery uses `DmxMovingHead*` metadata, motor channels, dimmer/shutter channels, advanced `Single Line` controls, and `WorldPosX` ordering.

```powershell
dotnet run --project tools/MovingHeadSequencer -- `
  --rgb-effects-path Red.zip `
  --inspect-layout
```

Target a subset with repeatable exact selectors:

```powershell
dotnet run --project tools/MovingHeadSequencer -- `
  --sequence-path "A-Christmas-Storm(old_layout).xsq" `
  --rgb-effects-path Red.zip `
  --head-count 4 `
  --include-fixture-group "Roof Movers" `
  --exclude-fixture "Roof MH 4" `
  --include-fixture "Side MH 1" `
  --force
```

Selectors are `--include-fixture`, `--exclude-fixture`, `--include-fixture-group`, and `--include-fixture-tag`. Unknown names or tags fail clearly. The final selected count must match `--head-count`. Original aggregate-control node indexes are preserved, so sparse subsets do not shift channels.

## Cue Sheets

External JSON cue sheets make choreography data-driven. See `examples/liljon-cue-sheet.example.json`.

A cue sheet contains:

- named reusable patterns
- contiguous named sections with `startMs` and `endMs`
- pan, tilt, dimmer, and shutter state
- defaults with per-section or named-pattern overrides

```powershell
dotnet run --project tools/MovingHeadSequencer -- `
  --sequence-path "All I Really Want For Christmas (feat.xsq" `
  --cue-sheet tools/MovingHeadSequencer/examples/liljon-cue-sheet.example.json `
  --dry-run
```

The file must cover the supported sequence duration exactly. Unknown patterns, gaps, overlaps, and incomplete states are rejected.

## Safety Validation

Every plan is validated before generation for:

- pan/tilt continuity and duration
- fixture-profile range violations
- excessive pan/tilt travel
- movement shorter than the configured minimum duration
- dimmer/shutter overlaps and visibility mismatches
- missing layout controls and fixture-count mismatches
- excessive dynamic-motion density

Preview without writing an XSQ:

```powershell
dotnet run --project tools/MovingHeadSequencer -- `
  --sequence-path "All I Really Want For Christmas (feat.xsq" `
  --dry-run `
  --validation-report reports/liljon-validation.json
```

Set `validation.treatWarningsAsErrors` to enforce warning-free generation in automation.

## Activity Audits

Audits report authored position, true curve-driven movement, per-head average motion, aggregate/group motion, positive dimmer, visible motion, near-dark movement, dimmed repositioning, shutter-open time, and meaningful rest gaps. Unknown intensity remains `unknown` instead of being inferred.

```powershell
# Human-readable corpus table
dotnet run --project tools/MovingHeadSequencer -- --audit-references

# One sequence as JSON
dotnet run --project tools/MovingHeadSequencer -- `
  --audit-references `
  --sequence-path "All-I-Really-Want-For-Christmas-4MH.xsq" `
  --audit-format json `
  --audit-output reports/liljon-audit.json

# Corpus CSV
dotnet run --project tools/MovingHeadSequencer -- `
  --audit-references `
  --audit-format csv `
  --audit-output reports/reference-audit.csv
```

Configure `audit.minimumGapMs`, `audit.nearDarkThresholdPercent`, and `audit.outputFormat` in JSON.

## Library Intelligence

Classify workspace sequences as active references, inactive placeholders, generated outputs, semantic duplicates, files without moving heads, or invalid XML:

```powershell
dotnet run --project tools/MovingHeadSequencer -- `
  --classify-sources `
  --classification-format json `
  --classification-output reports/source-library.json
```

Get conservative choreography suggestions from real timing labels and measured motion density:

```powershell
dotnet run --project tools/MovingHeadSequencer -- `
  --suggest-patterns `
  --sequence-path "Bloody Mary HD.xsq"
```

Suggestions favor parked verses and bridges, tilt-only builds, one broad movement at chorus arrivals, intensity accents for short calls, and a rest after phrase ramps.

## Migration Guide

| Previous workflow | New optional workflow |
| --- | --- |
| `--target All --head-count 4 --force` | Same command; behavior remains compatible without config |
| Repeated long CLI commands | Put stable values in `moving-head-sequencer.json` |
| One shared setup for all songs | Add filename keys under `sequenceOverrides` |
| Hard-coded fixture geometry | Define global and exact per-fixture profiles |
| Edit C# cue arrays | Use `--cue-sheet <file.json>` |
| Run one song at a time | Use `--manifest <file.json>` |
| Discover bad output after generation | Run `--dry-run --validation-report <file>` |
| Manually inspect moving-head usage | Run `--audit-references` with table, CSV, or JSON |
| Maintain reference lists manually | Run `--classify-sources` |

Start by running the old command with `--dry-run`. Add a project config only for values you need to change, then move song-specific values into `sequenceOverrides`. Existing commands and built-in cue definitions remain valid when the new files and flags are omitted.

## Publish

```powershell
dotnet publish tools/MovingHeadSequencer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish tools/MovingHeadSequencer -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish tools/MovingHeadSequencer -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

Published binaries are under `tools/MovingHeadSequencer/bin/Release/net8.0/<runtime>/publish/`.

Publish the visual editor as a self-contained local application:

```powershell
dotnet publish tools/MovingHeadSequencer.Web -c Release -r win-x64   --self-contained true -p:PublishSingleFile=true
dotnet publish tools/MovingHeadSequencer.Web -c Release -r osx-x64   --self-contained true -p:PublishSingleFile=true
dotnet publish tools/MovingHeadSequencer.Web -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

Run the published executable from the sequence directory, or pass `--workspace-root <path>`.
