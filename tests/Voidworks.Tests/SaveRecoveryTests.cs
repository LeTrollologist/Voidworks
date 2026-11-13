using System;
using System.IO;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Persistence;
using Xunit;

namespace Voidworks.Tests;

public class SaveRecoveryTests
{
    [Fact]
    public void FindInterruptedSaves_ReportsAValidTempFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            string target = Path.Combine(directory, "ship-save.json");
            string temp = WriteTemp(directory, target, SimulationSerializer.SaveToJson(CommandTestWorld.Create()));

            var candidates = SaveRecovery.FindInterruptedSaves(target);

            InterruptedSave candidate = Assert.Single(candidates);
            Assert.Equal(temp, candidate.TempPath);
            Assert.True(candidate.IsValid);
            Assert.NotNull(candidate.Summary);
            Assert.Equal(1, candidate.Summary!.CrewCount);
            Assert.Equal(SimulationSerializer.CurrentSaveFormatVersion, candidate.Summary.SaveFormatVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRecoverInterruptedSave_PromotesAValidTempFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            string target = Path.Combine(directory, "ship-save.json");
            WriteTemp(directory, target, SimulationSerializer.SaveToJson(CommandTestWorld.Create()));

            string? recovered = SaveRecovery.TryRecoverInterruptedSave(target, out string message);

            Assert.Equal(Path.GetFullPath(target), recovered);
            Assert.Contains("Recovered interrupted save", message);
            Assert.True(File.Exists(target));
            SimulationState loaded = SimulationSerializer.LoadFromFile(target);
            Assert.Single(loaded.Crew);
            Assert.Empty(SaveRecovery.FindInterruptedSaves(target));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRecoverInterruptedSave_RefusesInvalidTemps()
    {
        string directory = CreateTempDirectory();
        try
        {
            string target = Path.Combine(directory, "ship-save.json");
            WriteTemp(directory, target, "{ not a save }");

            InterruptedSave candidate = Assert.Single(SaveRecovery.FindInterruptedSaves(target));
            Assert.False(candidate.IsValid);
            Assert.NotNull(candidate.Error);

            string? recovered = SaveRecovery.TryRecoverInterruptedSave(target, out string message);

            Assert.Null(recovered);
            Assert.Contains("none is a valid save", message);
            Assert.False(File.Exists(target));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRecoverInterruptedSave_DoesNotOverwriteAnExistingSave()
    {
        string directory = CreateTempDirectory();
        try
        {
            string target = Path.Combine(directory, "ship-save.json");
            SimulationSerializer.SaveToFile(CommandTestWorld.Create(), target);
            WriteTemp(directory, target, SimulationSerializer.SaveToJson(CommandTestWorld.Create()));

            string? recovered = SaveRecovery.TryRecoverInterruptedSave(target, out string message);

            Assert.Null(recovered);
            Assert.Contains("already exists", message);
            Assert.Single(SaveRecovery.FindInterruptedSaves(target));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Describe_SummarizesTheSave()
    {
        string directory = CreateTempDirectory();
        try
        {
            string target = Path.Combine(directory, "ship-save.json");
            SimulationSerializer.SaveToFile(CommandTestWorld.Create(), target);

            SaveSummary summary = SaveRecovery.Describe(target);

            Assert.Equal(Path.GetFullPath(target), summary.Path);
            Assert.Equal(SimulationSerializer.CurrentSaveFormatVersion, summary.SaveFormatVersion);
            Assert.Equal(1, summary.CrewCount);
            Assert.Null(summary.ScenarioId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadFromFile_NamesTheFailingFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            string target = Path.Combine(directory, "broken-save.json");
            File.WriteAllText(target, "{ definitely not a save }");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => SimulationSerializer.LoadFromFile(target));

            Assert.Contains(Path.GetFullPath(target), exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindInterruptedSaves_ReturnsNewestFirst()
    {
        string directory = CreateTempDirectory();
        try
        {
            string target = Path.Combine(directory, "ship-save.json");
            string older = WriteTemp(directory, target, SimulationSerializer.SaveToJson(CommandTestWorld.Create()));
            string newer = WriteTemp(directory, target, SimulationSerializer.SaveToJson(CommandTestWorld.Create()));
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

            var candidates = SaveRecovery.FindInterruptedSaves(target);

            Assert.Equal(2, candidates.Count);
            Assert.Equal(newer, candidates[0].TempPath);
            Assert.Equal(older, candidates[1].TempPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "voidworks-save-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteTemp(string directory, string target, string json)
    {
        string fileName = Path.GetFileName(target);
        string temp = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, json);
        return Path.GetFullPath(temp);
    }
}
