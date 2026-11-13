using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Voidworks.Sim.Persistence;

/// <summary>Compact description of a save file's contents.</summary>
public sealed record SaveSummary(
    string Path,
    int SaveFormatVersion,
    double SimTime,
    string? ScenarioId,
    int CrewCount,
    int MachineCount,
    int JobCount,
    int LooseItemCount);

/// <summary>
/// A leftover temporary file from an interrupted save. Valid candidates contain a
/// complete save that never replaced its destination.
/// </summary>
public sealed record InterruptedSave(
    string TempPath,
    DateTime LastWriteTimeUtc,
    bool IsValid,
    SaveSummary? Summary,
    string? Error);

/// <summary>
/// Detection and recovery for saves interrupted between the temporary write and the
/// atomic replace. The serializer writes <c>.&lt;name&gt;.&lt;guid&gt;.tmp</c> beside the
/// destination, so an abandoned file with that pattern is the only surviving copy
/// when the process died at the wrong moment.
/// </summary>
public static class SaveRecovery
{
    public static IReadOnlyList<InterruptedSave> FindInterruptedSaves(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        string fullPath = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(fullPath)!;
        string fileName = Path.GetFileName(fullPath);
        if (!Directory.Exists(directory))
            return Array.Empty<InterruptedSave>();

        string pattern = $".{fileName}.*.tmp";
        var candidates = new List<InterruptedSave>();
        foreach (string temp in Directory.EnumerateFiles(directory, pattern))
        {
            try
            {
                SimulationState state = SimulationSerializer.LoadFromJson(File.ReadAllText(temp));
                candidates.Add(new InterruptedSave(
                    temp,
                    File.GetLastWriteTimeUtc(temp),
                    true,
                    Summarize(temp, state, ReadSaveFormatVersion(temp)),
                    null));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException
                                                  or NotSupportedException or UnauthorizedAccessException)
            {
                candidates.Add(new InterruptedSave(
                    temp,
                    File.GetLastWriteTimeUtc(temp),
                    false,
                    null,
                    exception.Message));
            }
        }

        return candidates.OrderByDescending(candidate => candidate.LastWriteTimeUtc).ToList();
    }

    /// <summary>
    /// Promotes the newest valid abandoned temporary file to <paramref name="targetPath"/>.
    /// Returns the recovered path, or null when there is nothing safe to recover.
    /// </summary>
    public static string? TryRecoverInterruptedSave(string targetPath, out string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        string fullPath = Path.GetFullPath(targetPath);

        if (File.Exists(fullPath))
        {
            message = $"Save already exists at {fullPath}; nothing to recover.";
            return null;
        }

        IReadOnlyList<InterruptedSave> candidates = FindInterruptedSaves(fullPath);
        if (candidates.Count == 0)
        {
            message = $"No abandoned temporary save found for {fullPath}.";
            return null;
        }

        InterruptedSave? valid = candidates.FirstOrDefault(candidate => candidate.IsValid);
        if (valid == null)
        {
            message = $"{candidates.Count} abandoned temporary file(s) found, but none is a valid save.";
            return null;
        }

        try
        {
            File.Move(valid.TempPath, fullPath);
            message = $"Recovered interrupted save from {valid.TempPath}.";
            return fullPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Failed to recover {valid.TempPath}: {exception.Message}";
            return null;
        }
    }

    public static SaveSummary Describe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        return Summarize(fullPath, SimulationSerializer.LoadFromFile(fullPath), ReadSaveFormatVersion(fullPath));
    }

    private static SaveSummary Summarize(string path, SimulationState state, int saveFormatVersion)
        => new(
            path,
            saveFormatVersion,
            state.SimTime,
            state.ActiveScenario?.Id,
            state.Crew.Count,
            state.Machines.Count,
            state.JobManager.ActiveJobs.Count,
            state.JobManager.WorldItems.Count);

    private static int ReadSaveFormatVersion(string path)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            if (root is JsonObject obj && obj["SaveFormatVersion"] is JsonNode node)
                return node.GetValue<int>();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
        }
        return 0;
    }
}
