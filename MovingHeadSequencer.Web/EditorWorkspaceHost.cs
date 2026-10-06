namespace MovingHeadSequencer.Web;

internal sealed class EditorWorkspaceHost : IEditorService
{
    private readonly WorkspacePreferenceStore preferenceStore;
    private EditorService current;

    public EditorWorkspaceHost(string workspaceRoot, WorkspacePreferenceStore preferenceStore)
    {
        this.preferenceStore = preferenceStore;
        current = new EditorService(workspaceRoot);
    }

    public string WorkspaceRoot => Current.WorkspaceRoot;

    public WorkspaceSwitchResult Switch(WorkspaceSwitchRequest request)
    {
        if (!Path.IsPathFullyQualified(request.Path))
        {
            throw new InvalidDataException("Enter an absolute workspace path.");
        }
        var replacement = new EditorService(Path.GetFullPath(request.Path));
        replacement.Bootstrap(null, request.HeadCount);
        preferenceStore.Save(replacement.WorkspaceRoot);
        Interlocked.Exchange(ref current, replacement);
        return new WorkspaceSwitchResult(false, replacement.WorkspaceRoot);
    }

    public WorkspaceSwitchResult PickAndSwitch(int headCount)
    {
        var selected = NativeFolderPicker.Pick(WorkspaceRoot);
        return selected is null
            ? new WorkspaceSwitchResult(true, WorkspaceRoot)
            : Switch(new WorkspaceSwitchRequest(selected, headCount));
    }

    private EditorService Current => Volatile.Read(ref current);

    public EditorDocument Bootstrap(string? sequenceId, int headCount) => Current.Bootstrap(sequenceId, headCount);
    public EditorDocument Compile(EditorRequest request) => Current.Compile(request);
    public CueRegenerationResult Suggest(CueRegenerationRequest request) => Current.Suggest(request);
    public WholeSongRegenerationResult Regenerate(WholeSongRegenerationRequest request) => Current.Regenerate(request);
    public GenerationResult Generate(EditorRequest request) => Current.Generate(request);
    public SequenceBackupCatalog ListBackups(string sequenceId) => Current.ListBackups(sequenceId);
    public OriginalGenerationResult GenerateOriginal(EditorRequest request) => Current.GenerateOriginal(request);
    public RestoreBackupResult RestoreBackup(RestoreBackupRequest request) => Current.RestoreBackup(request);
    public StudioProjectLoadResult LoadProject(string sequenceId, int headCount) => Current.LoadProject(sequenceId, headCount);
    public SaveStudioProjectResult SaveProject(SaveStudioProjectRequest request) => Current.SaveProject(request);
    public void ResetProject(ResetStudioProjectRequest request) => Current.ResetProject(request);
}