using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovingHeadSequencer.Web;

internal sealed class SequenceProjectStore
{
    public const string ProjectFormat = "moving-head-studio-project";
    public const int ProjectVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string workspaceRoot;
    private readonly string projectsRoot;
    private readonly object syncRoot = new();

    public SequenceProjectStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        projectsRoot = Path.Combine(this.workspaceRoot, ".moving-head-sequencer", "projects");
    }

    public StudioProject? Load(string sourcePath, int headCount)
    {
        lock (syncRoot)
        {
            var path = GetProjectPath(sourcePath, headCount);
            return File.Exists(path) ? ReadProject(path, sourcePath, headCount) : null;
        }
    }

    public void Save(string sourcePath, StudioProject project, string? expectedRevision)
    {
        lock (syncRoot)
        {
            Validate(project, sourcePath, project.Editor.HeadCount);
            var path = GetProjectPath(sourcePath, project.Editor.HeadCount);
            var current = File.Exists(path)
                ? ReadProject(path, sourcePath, project.Editor.HeadCount)
                : null;
            if (!string.Equals(current?.Revision, expectedRevision, StringComparison.Ordinal))
            {
                throw new IOException(
                    "The hidden project changed in another editor tab; reload before saving.");
            }
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var stagingPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(
                    stagingPath,
                    JsonSerializer.Serialize(project, JsonOptions),
                    new UTF8Encoding(false));
                var staged = ReadProject(stagingPath, sourcePath, project.Editor.HeadCount);
                if (!staged.Revision.Equals(project.Revision, StringComparison.Ordinal))
                {
                    throw new IOException("The staged project revision does not match the save request.");
                }
                if (File.Exists(path))
                {
                    var archivePath = CreateArchivePath(directory, project.Editor.HeadCount);
                    File.Replace(stagingPath, path, archivePath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(stagingPath, path);
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

    public void Archive(string sourcePath, int headCount)
    {
        lock (syncRoot)
        {
            var path = GetProjectPath(sourcePath, headCount);
            if (!File.Exists(path))
            {
                return;
            }
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.Move(path, CreateArchivePath(directory, headCount));
        }
    }

    public void ArchiveAll(string sourcePath)
    {
        lock (syncRoot)
        {
            var directory = GetSourceDirectory(sourcePath);
            if (!Directory.Exists(directory))
            {
                return;
            }
            foreach (var path in Directory.EnumerateFiles(directory, "*.mhproj", SearchOption.TopDirectoryOnly))
            {
                var headCountText = Path.GetFileNameWithoutExtension(path);
                var headCount = int.TryParse(headCountText, out var parsed) ? parsed : 0;
                File.Move(path, CreateArchivePath(directory, headCount));
            }
        }
    }

    private StudioProject ReadProject(string path, string sourcePath, int headCount)
    {
        var text = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<StudioProject>(text, JsonOptions)
            ?? throw new InvalidDataException($"Hidden project is empty: {path}");
        if (string.IsNullOrWhiteSpace(project.Revision))
        {
            project = project with
            {
                Revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
            };
        }
        Validate(project, sourcePath, headCount);
        return project;
    }

    private void Validate(StudioProject project, string sourcePath, int headCount)
    {
        sourcePath = ValidateSourcePath(sourcePath);
        if (!project.Format.Equals(ProjectFormat, StringComparison.Ordinal) ||
            project.Version != ProjectVersion)
        {
            throw new InvalidDataException(
                $"Unsupported hidden project format/version: {project.Format} v{project.Version}.");
        }
        if (string.IsNullOrWhiteSpace(project.Revision))
        {
            throw new InvalidDataException("Hidden project revision is missing.");
        }
        if (project.Editor.HeadCount != headCount)
        {
            throw new InvalidDataException(
                $"Hidden project head count {project.Editor.HeadCount} does not match {headCount}.");
        }
        if (!project.Source.FileName.Equals(Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Hidden project source filename does not match its linked XSQ.");
        }
        if (project.Source.DurationMs != project.Editor.CueSheet.DurationMs)
        {
            throw new InvalidDataException("Hidden project source and cue-sheet durations do not match.");
        }
        project.Editor.CueSheet.Validate("hidden project");
        project.Editor.FixtureProfile.Validate("hidden project");
        if (project.Preview.FixtureRotations.Count != headCount ||
            project.Preview.FixtureRotations.Any(rotation => rotation is not (0 or 90 or 180 or 270)))
        {
            throw new InvalidDataException("Hidden project preview rotations are invalid.");
        }
        if (project.Preview.MountMode is not ("floor" or "truss"))
        {
            throw new InvalidDataException("Hidden project mount mode is invalid.");
        }
    }

    private string GetProjectPath(string sourcePath, int headCount) =>
        Path.Combine(GetSourceDirectory(sourcePath), $"{headCount}.mhproj");

    private string GetSourceDirectory(string sourcePath)
    {
        sourcePath = ValidateSourcePath(sourcePath);
        var relative = Path.GetRelativePath(workspaceRoot, sourcePath);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative.ToUpperInvariant())))[..20];
        return Path.Combine(projectsRoot, key);
    }

    private string ValidateSourcePath(string sourcePath)
    {
        var path = Path.GetFullPath(sourcePath);
        if (!path.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(path).Equals(".xsq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A hidden project must link to an XSQ inside the workspace.");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Project source was not found: {path}", path);
        }
        return path;
    }

    private static string CreateArchivePath(string directory, int headCount)
    {
        var archiveDirectory = Path.Combine(directory, "archive");
        Directory.CreateDirectory(archiveDirectory);
        var timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd'T'HHmmssfffffff'Z'",
            CultureInfo.InvariantCulture);
        return Path.Combine(archiveDirectory, $"{headCount}-{timestamp}-{Guid.NewGuid():N}.mhproj");
    }
}