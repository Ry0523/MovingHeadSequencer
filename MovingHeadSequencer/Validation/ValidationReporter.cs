using System.Text.Json;

namespace MovingHeadSequencer.Validation;

internal static class ValidationReporter
{
    public static void WriteSummary(SequenceValidationReport report, TextWriter output)
    {
        output.WriteLine($"Validation: {Path.GetFileName(report.Sequence)} -> {Path.GetFileName(report.Output)}");
        output.WriteLine(
            $"  heads={report.HeadCount}, profile={report.FixtureProfile}, " +
            $"pan-time-moving={report.Metrics.PanDynamicPercent:0.0}%, " +
            $"tilt-time-moving={report.Metrics.TiltDynamicPercent:0.0}%, " +
            $"visible={report.Metrics.DimmerVisiblePercent:0.0}%, " +
            $"shutter={report.Metrics.ShutterOpenPercent:0.0}%");
        output.WriteLine(
            $"  pan-range={report.Metrics.PanMinimum}-{report.Metrics.PanMaximum} DMX, " +
            $"tilt-range={report.Metrics.TiltMinimum}-{report.Metrics.TiltMaximum} DMX, " +
            $"max-pan-delta={report.Metrics.MaximumPanDelta}, " +
            $"max-tilt-delta={report.Metrics.MaximumTiltDelta}, " +
            $"blackout-windows={report.Metrics.BlackoutWindowCount}");
        foreach (var issue in report.Issues)
        {
            var interval = issue.Start is null ? string.Empty : $" [{issue.Start}-{issue.End} ms]";
            output.WriteLine($"  {issue.Severity}: {issue.Code}{interval}: {issue.Message}");
        }
    }

    public static void WriteJson(string path, IReadOnlyList<SequenceValidationReport> reports)
    {
        var resolvedPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        File.WriteAllText(
            resolvedPath,
            JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
}