using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Configuration;
using MovingHeadSequencer.Preview;
using MovingHeadSequencer.Timing;
using MovingHeadSequencer.Validation;

namespace MovingHeadSequencer.Web;

/// <summary>Identifies a sequence supported by the visual editor.</summary>
internal sealed record SequenceOption(string Id, string Name, string FileName, string OutputBaseName);

/// <summary>Describes the audio file declared by the source sequence.</summary>
internal sealed record SequenceMediaInfo(
    string? DeclaredPath,
    string? FileName,
    bool Exists,
    bool RequiresRelink);

/// <summary>Contains an editable cue sheet and generation options.</summary>
internal sealed record EditorRequest(
    string SequenceId,
    int HeadCount,
    CueSheet CueSheet,
    FixtureProfileSettings? FixtureProfile = null,
    string? OutputFileName = null,
    bool Force = false,
    string? TimingSourceName = null,
    string? ExpectedSourceFingerprint = null);

/// <summary>Requests ranked replacement choices for one editable cue.</summary>
internal sealed record CueRegenerationRequest(EditorRequest Editor, int CueIndex);

/// <summary>Describes one explainable choreography replacement.</summary>
internal sealed record CueRegenerationChoice(
    string Id,
    string Name,
    string Energy,
    int Confidence,
    string Rationale,
    IReadOnlyList<string> Evidence,
    PanPattern Pan,
    TiltPattern Tilt,
    DimmerPattern Dimmer,
    RhythmPattern Rhythm,
    MotionEnergy MotionEnergy,
    MotionShape MotionShape,
    MotionPhase MotionPhase,
    IntensityEnvelope IntensityEnvelope,
    bool ShutterOpen);

/// <summary>Contains cue context and ranked regeneration choices.</summary>
internal sealed record CueRegenerationResult(
    int CueIndex,
    string ContextLabel,
    int DurationMs,
    int BeatCount,
    IReadOnlyList<string> ConcurrentEffects,
    IReadOnlyList<CueRegenerationChoice> Choices);

/// <summary>Requests an atomic ranked regeneration of every cue.</summary>
internal sealed record WholeSongRegenerationRequest(EditorRequest Editor);

/// <summary>Summarizes one choice applied during whole-song regeneration.</summary>
internal sealed record AppliedRegenerationChoice(
    int CueIndex,
    string ChoiceId,
    string Name,
    string Energy,
    int Confidence,
    string Rationale,
    IReadOnlyList<string> Evidence,
    PanPattern Pan,
    TiltPattern Tilt,
    DimmerPattern Dimmer,
    RhythmPattern Rhythm,
    MotionEnergy MotionEnergy,
    MotionShape MotionShape,
    MotionPhase MotionPhase,
    IntensityEnvelope IntensityEnvelope,
    bool ShutterOpen);

/// <summary>Contains the validated regenerated document and an application summary.</summary>
internal sealed record WholeSongRegenerationResult(
    EditorDocument Document,
    IReadOnlyList<AppliedRegenerationChoice> AppliedChoices,
    double AverageConfidence);

/// <summary>Contains the editable document, compiled preview tracks, and validation state.</summary>
internal sealed record EditorDocument(
    IReadOnlyList<SequenceOption> Sequences,
    string WorkspaceRoot,
    string SequenceId,
    string SequenceName,
    string SourceFileName,
    string SourceFingerprint,
    int HeadCount,
    string SuggestedOutputFileName,
    FixtureProfileSettings FixtureProfile,
    CueSheet CueSheet,
    CompiledPreview Preview,
    TimingMap Timing,
    SequenceMediaInfo Media,
    SequenceValidationReport Validation,
    IReadOnlyList<string> PanPatterns,
    IReadOnlyList<string> TiltPatterns,
    IReadOnlyList<string> DimmerPatterns,
    IReadOnlyList<string> RhythmPatterns,
    IReadOnlyList<string> MotionEnergies,
    IReadOnlyList<string> MotionShapes,
    IReadOnlyList<string> MotionPhases,
    IReadOnlyList<string> IntensityEnvelopes);

/// <summary>Describes a successfully generated local XSQ file.</summary>
internal sealed record GenerationResult(
    string OutputPath,
    SequenceValidationReport Validation,
    IReadOnlyList<string> Messages);

/// <summary>Describes one restorable original-sequence backup.</summary>
internal sealed record SequenceBackupInfo(
    string Id,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes);

/// <summary>Lists backups available for one source sequence.</summary>
internal sealed record SequenceBackupCatalog(
    string SequenceId,
    string SourceFileName,
    IReadOnlyList<SequenceBackupInfo> Backups);

/// <summary>Reports an original-sequence replacement and its verified backup.</summary>
internal sealed record OriginalGenerationResult(
    string SourcePath,
    string SourceFingerprint,
    SequenceBackupInfo Backup,
    SequenceValidationReport Validation,
    IReadOnlyList<string> Messages);

/// <summary>Requests restoration of one source backup.</summary>
internal sealed record RestoreBackupRequest(string SequenceId, string BackupId);

/// <summary>Reports a restored source and the safety backup made before restoration.</summary>
internal sealed record RestoreBackupResult(
    string SourcePath,
    SequenceBackupInfo RestoredBackup,
    SequenceBackupInfo PreviousVersionBackup);

/// <summary>Identifies the XSQ revision associated with a hidden editor project.</summary>
internal sealed record StudioProjectSource(
    string SequenceId,
    string FileName,
    string Name,
    string Fingerprint,
    int DurationMs);

/// <summary>Contains editable sequence state persisted by Moving Head Studio.</summary>
internal sealed record StudioProjectEditor(
    int HeadCount,
    string? TimingSourceName,
    string? OutputFileName,
    FixtureProfileSettings FixtureProfile,
    CueSheet CueSheet);

/// <summary>Contains preview-only settings persisted with a hidden project.</summary>
internal sealed record StudioProjectPreview(
    string MountMode,
    IReadOnlyList<int> FixtureRotations);

/// <summary>Contains the last editor position persisted with a hidden project.</summary>
internal sealed record StudioProjectView(int SelectedCueIndex, int CurrentMs);

/// <summary>Summarizes the most recent whole-song regeneration.</summary>
internal sealed record StudioProjectRegenerationSummary(
    int CueCount,
    double AverageConfidence,
    IReadOnlyList<string> Distribution);

/// <summary>Contains generated-choice attribution persisted with a hidden project.</summary>
internal sealed record StudioProjectRegeneration(
    StudioProjectRegenerationSummary? Summary,
    IReadOnlyList<AppliedRegenerationChoice>? AppliedChoices);

/// <summary>Versioned hidden Moving Head Studio project file.</summary>
internal sealed record StudioProject(
    string Format,
    int Version,
    string Revision,
    DateTimeOffset SavedAtUtc,
    StudioProjectSource Source,
    StudioProjectEditor Editor,
    StudioProjectPreview Preview,
    StudioProjectView View,
    StudioProjectRegeneration Regeneration);

/// <summary>Returns a hidden project when one exists for a sequence/head count.</summary>
internal sealed record StudioProjectLoadResult(StudioProject? Project, string? Warning = null);

/// <summary>Requests atomic persistence of a compiled editor project.</summary>
internal sealed record SaveStudioProjectRequest(
    EditorRequest Editor,
    string ExpectedSourceFingerprint,
    string? ExpectedProjectRevision,
    StudioProjectPreview Preview,
    StudioProjectView View,
    StudioProjectRegeneration Regeneration);

/// <summary>Reports a successful hidden project save.</summary>
internal sealed record SaveStudioProjectResult(
    DateTimeOffset SavedAtUtc,
    string SourceFingerprint,
    string ProjectRevision);

/// <summary>Requests rebuilding from an XSQ instead of its hidden project.</summary>
internal sealed record ResetStudioProjectRequest(string SequenceId, int HeadCount);

/// <summary>Requests switching the process-wide sequence workspace.</summary>
internal sealed record WorkspaceSwitchRequest(string Path, int HeadCount = 4);

/// <summary>Reports the active workspace after a switch or cancelled picker.</summary>
internal sealed record WorkspaceSwitchResult(bool Cancelled, string WorkspaceRoot);

internal interface IEditorService
{
    EditorDocument Bootstrap(string? sequenceId, int headCount);
    EditorDocument Compile(EditorRequest request);
    CueRegenerationResult Suggest(CueRegenerationRequest request);
    WholeSongRegenerationResult Regenerate(WholeSongRegenerationRequest request);
    GenerationResult Generate(EditorRequest request);
    SequenceBackupCatalog ListBackups(string sequenceId);
    OriginalGenerationResult GenerateOriginal(EditorRequest request);
    RestoreBackupResult RestoreBackup(RestoreBackupRequest request);
    StudioProjectLoadResult LoadProject(string sequenceId, int headCount);
    SaveStudioProjectResult SaveProject(SaveStudioProjectRequest request);
    void ResetProject(ResetStudioProjectRequest request);
}
