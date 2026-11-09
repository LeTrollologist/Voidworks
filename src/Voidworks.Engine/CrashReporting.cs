using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voidworks.Engine;

/// <summary>
/// Writes fatal-error reports so a crashed session leaves something diagnosable.
/// Reports are plain text and land in <c>%LOCALAPPDATA%\Voidworks\logs</c> by default.
/// </summary>
public static class CrashReporting
{
    public const string LogFolderName = "logs";

    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voidworks",
        LogFolderName);

    /// <summary>
    /// Installs process-wide handlers. The handler cannot stop termination; it only
    /// makes a best-effort report before the process exits.
    /// </summary>
    public static void Install(Func<IReadOnlyList<string>>? recentEvents = null)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            TryWrite(args.ExceptionObject as Exception, recentEvents);

        TaskScheduler.UnobservedTaskException += (_, args) =>
            TryWrite(args.Exception, recentEvents);
    }

    /// <summary>Writes a report and returns the full path, or null if writing failed.</summary>
    public static string? TryWrite(
        Exception? exception,
        Func<IReadOnlyList<string>>? recentEvents = null,
        string? logDirectory = null,
        string? commandLine = null)
    {
        try
        {
            string path = WriteReport(
                exception ?? new InvalidOperationException("Unknown fatal error."),
                logDirectory,
                commandLine,
                recentEvents?.Invoke());
            Console.Error.WriteLine($"[Crash] Fatal error report written to {path}");
            return path;
        }
        catch (Exception writeFailure)
        {
            Console.Error.WriteLine($"[Crash] Failed to write a crash report: {writeFailure.Message}");
            return null;
        }
    }

    public static string WriteReport(
        Exception exception,
        string? logDirectory = null,
        string? commandLine = null,
        IReadOnlyList<string>? recentEvents = null,
        DateTimeOffset? timestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string directory = string.IsNullOrWhiteSpace(logDirectory) ? DefaultLogDirectory : logDirectory;
        Directory.CreateDirectory(directory);

        DateTimeOffset timestamp = timestampUtc ?? DateTimeOffset.UtcNow;
        string fileName = $"crash-{timestamp:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log";
        string path = Path.Combine(directory, fileName);

        File.WriteAllText(
            path,
            BuildReport(exception, timestamp, commandLine ?? SafeCommandLine(), recentEvents),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string BuildReport(
        Exception exception,
        DateTimeOffset timestampUtc,
        string? commandLine,
        IReadOnlyList<string>? recentEvents)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var report = new StringBuilder();
        report.AppendLine($"Voidworks {BuildInfo.Version} crash report");
        report.AppendLine("============================");
        report.AppendLine($"UTC time: {timestampUtc:yyyy-MM-ddTHH:mm:ssZ}");
        report.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        report.AppendLine($"Runtime: .NET {Environment.Version}");
        report.AppendLine($"Process: {Environment.ProcessId}");
        report.AppendLine($"Command: {commandLine ?? "(unknown)"}");
        report.AppendLine();
        report.AppendLine("Exception");
        report.AppendLine("---------");
        report.AppendLine(exception.ToString());

        if (recentEvents is { Count: > 0 })
        {
            report.AppendLine();
            report.AppendLine($"Recent simulation events (last {recentEvents.Count})");
            report.AppendLine("----------------------------------------");
            foreach (string entry in recentEvents)
                report.AppendLine(entry);
        }

        return report.ToString();
    }

    private static string SafeCommandLine()
    {
        try
        {
            return Environment.CommandLine;
        }
        catch (Exception)
        {
            return "(unavailable)";
        }
    }
}
