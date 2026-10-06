using System.Diagnostics;

namespace MovingHeadSequencer.Web;

internal static class NativeFolderPicker
{
    public static string? Pick(string currentPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return PickWindows(currentPath);
        }
        if (OperatingSystem.IsMacOS())
        {
            return PickMacOs(currentPath);
        }
        throw new InvalidDataException(
            "A native folder picker is not available on this platform; enter an absolute path manually.");
    }

    private static string? PickWindows(string currentPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-STA");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "Add-Type -AssemblyName System.Windows.Forms; " +
            "$dialog = New-Object System.Windows.Forms.FolderBrowserDialog; " +
            "$dialog.Description = 'Select an xLights sequence workspace'; " +
            "$dialog.ShowNewFolderButton = $false; " +
            "$dialog.SelectedPath = $env:MOVING_HEAD_CURRENT_WORKSPACE; " +
            "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) " +
            "{ [Console]::Out.Write($dialog.SelectedPath) }");
        startInfo.Environment["MOVING_HEAD_CURRENT_WORKSPACE"] = currentPath;
        return RunPicker(startInfo);
    }

    private static string? PickMacOs(string currentPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(
            "POSIX path of (choose folder with prompt \"Select an xLights sequence workspace\" " +
            "default location POSIX file (system attribute \"MOVING_HEAD_CURRENT_WORKSPACE\"))");
        startInfo.Environment["MOVING_HEAD_CURRENT_WORKSPACE"] = currentPath;
        return RunPicker(startInfo, cancellationExitCode: 1);
    }

    private static string? RunPicker(ProcessStartInfo startInfo, int? cancellationExitCode = null)
    {
        using var process = Process.Start(startInfo)
            ?? throw new IOException("The native folder picker could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (cancellationExitCode == process.ExitCode ||
            (process.ExitCode == 0 && string.IsNullOrWhiteSpace(output)))
        {
            return null;
        }
        if (process.ExitCode != 0)
        {
            throw new IOException(
                $"The native folder picker failed: {error.Trim()}");
        }
        return Path.GetFullPath(output.Trim());
    }
}