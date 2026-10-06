using System.Text;

namespace MovingHeadSequencer.Web;

internal sealed class WorkspacePreferenceStore
{
    private readonly string preferencePath;

    public WorkspacePreferenceStore(string? preferencePath = null)
    {
        this.preferencePath = preferencePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MovingHeadSequencer",
            "workspace-root.txt");
    }

    public string? Load()
    {
        try
        {
            if (!File.Exists(preferencePath))
            {
                return null;
            }
            var value = File.ReadAllText(preferencePath).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string workspaceRoot)
    {
        var directory = Path.GetDirectoryName(preferencePath)!;
        Directory.CreateDirectory(directory);
        var stagingPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(stagingPath, Path.GetFullPath(workspaceRoot), new UTF8Encoding(false));
            if (File.Exists(preferencePath))
            {
                File.Replace(stagingPath, preferencePath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(stagingPath, preferencePath);
            }
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }
}