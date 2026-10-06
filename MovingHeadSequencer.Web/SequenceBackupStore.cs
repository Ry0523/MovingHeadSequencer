using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace MovingHeadSequencer.Web;

internal sealed class SequenceBackupStore
{
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfffffff'Z'";
    private readonly string workspaceRoot;
    private readonly string storageRoot;

    public SequenceBackupStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        storageRoot = Path.Combine(this.workspaceRoot, ".moving-head-sequencer");
    }

    public string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public string CreateStagingPath()
    {
        var directory = Path.Combine(storageRoot, "staging");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.xsq");
    }

    public IReadOnlyList<SequenceBackupInfo> List(string sourcePath)
    {
        var directory = GetBackupDirectory(sourcePath);
        if (!Directory.Exists(directory))
        {
            return [];
        }
        return Directory.EnumerateFiles(directory, "*.xsq", SearchOption.TopDirectoryOnly)
            .Select(CreateInfo)
            .OrderByDescending(backup => backup.CreatedAtUtc)
            .ToArray();
    }

    public SequenceBackupInfo ReplaceOriginal(
        string sourcePath,
        string stagingPath,
        string expectedSourceFingerprint)
    {
        sourcePath = ValidateSourcePath(sourcePath);
        ValidateXsq(stagingPath);
        EnsureUnchanged(sourcePath, expectedSourceFingerprint);
        var backup = CreateVerifiedBackup(sourcePath, expectedSourceFingerprint);
        EnsureUnchanged(sourcePath, expectedSourceFingerprint);
        File.Replace(stagingPath, sourcePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        ValidateXsq(sourcePath);
        return backup;
    }

    public RestoreBackupResult Restore(
        string sourcePath,
        string backupId)
    {
        sourcePath = ValidateSourcePath(sourcePath);
        var selectedPath = ResolveBackupPath(sourcePath, backupId);
        ValidateXsq(selectedPath);
        var sourceFingerprint = Fingerprint(sourcePath);
        var selectedFingerprint = Fingerprint(selectedPath);
        var previous = CreateVerifiedBackup(sourcePath, sourceFingerprint);
        EnsureUnchanged(sourcePath, sourceFingerprint);

        var stagingPath = CreateStagingPath();
        try
        {
            File.Copy(selectedPath, stagingPath, overwrite: false);
            ValidateXsq(stagingPath);
            if (!Fingerprint(stagingPath).Equals(selectedFingerprint, StringComparison.Ordinal))
            {
                throw new IOException("The staged restore copy does not match the selected backup.");
            }
            File.Replace(stagingPath, sourcePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            ValidateXsq(sourcePath);
            return new RestoreBackupResult(sourcePath, CreateInfo(selectedPath), previous);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }

    private SequenceBackupInfo CreateVerifiedBackup(string sourcePath, string expectedFingerprint)
    {
        var directory = GetBackupDirectory(sourcePath);
        Directory.CreateDirectory(directory);
        var timestamp = DateTimeOffset.UtcNow;
        var id = $"{timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.xsq";
        var backupPath = Path.Combine(directory, id);
        try
        {
            File.Copy(sourcePath, backupPath, overwrite: false);
            ValidateXsq(backupPath);
            if (!Fingerprint(backupPath).Equals(expectedFingerprint, StringComparison.Ordinal))
            {
                throw new IOException("The backup does not match the original sequence.");
            }
            return new SequenceBackupInfo(id, timestamp, new FileInfo(backupPath).Length);
        }
        catch
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            throw;
        }
    }

    private void EnsureUnchanged(string sourcePath, string expectedFingerprint)
    {
        if (!Fingerprint(sourcePath).Equals(expectedFingerprint, StringComparison.Ordinal))
        {
            throw new IOException(
                "The original sequence changed while generation was in progress; no replacement was made.");
        }
    }

    private string ResolveBackupPath(string sourcePath, string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId) ||
            !backupId.Equals(Path.GetFileName(backupId), StringComparison.Ordinal) ||
            !Path.GetExtension(backupId).Equals(".xsq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Invalid backup identifier.");
        }
        var directory = GetBackupDirectory(sourcePath);
        var path = Path.GetFullPath(Path.Combine(directory, backupId));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Invalid backup path.");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Sequence backup was not found: {backupId}", path);
        }
        return path;
    }

    private string GetBackupDirectory(string sourcePath)
    {
        sourcePath = ValidateSourcePath(sourcePath);
        var relative = Path.GetRelativePath(workspaceRoot, sourcePath);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative.ToUpperInvariant())))[..20];
        return Path.Combine(storageRoot, "backups", key);
    }

    private string ValidateSourcePath(string sourcePath)
    {
        var path = Path.GetFullPath(sourcePath);
        if (!path.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(path).Equals(".xsq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The original sequence must be an XSQ file inside the workspace.");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Original sequence was not found: {path}", path);
        }
        return path;
    }

    private static SequenceBackupInfo CreateInfo(string path)
    {
        var id = Path.GetFileName(path);
        var timestampText = id.Split('-', 2)[0];
        if (!DateTimeOffset.TryParseExact(
                timestampText,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            timestamp = File.GetCreationTimeUtc(path);
        }
        return new SequenceBackupInfo(id, timestamp, new FileInfo(path).Length);
    }

    private static void ValidateXsq(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
        using var reader = XmlReader.Create(path, settings);
        reader.MoveToContent();
        if (!reader.Name.Equals("xsequence", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Sequence backup '{path}' does not contain an xsequence root.");
        }
    }
}