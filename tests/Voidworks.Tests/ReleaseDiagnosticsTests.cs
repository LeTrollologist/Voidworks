using System;
using System.IO;
using System.Linq;
using Voidworks.Engine;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class ReleaseDiagnosticsTests
{
    [Fact]
    public void TickBenchmark_Summarize_ComputesPercentilesAndTotals()
    {
        double[] samples = [1.0, 2.0, 3.0, 4.0, 5.0];

        TickBenchmarkResult result = TickBenchmark.Summarize(samples, allocatedBytes: 2048);

        Assert.Equal(5, result.Ticks);
        Assert.Equal(15.0, result.TotalMilliseconds, 6);
        Assert.Equal(3.0, result.MeanMilliseconds, 6);
        Assert.Equal(3.0, result.P50Milliseconds, 6);
        Assert.Equal(5.0, result.P95Milliseconds, 6);
        Assert.Equal(5.0, result.MaxMilliseconds, 6);
        Assert.Equal(0.5, result.SimulatedSeconds, 6);
        Assert.Equal(2048.0 / 1024.0 / 5.0, result.AllocatedKilobytesPerTick, 6);
    }

    [Fact]
    public void TickBenchmark_Summarize_HandlesSingleSample()
    {
        TickBenchmarkResult result = TickBenchmark.Summarize([0.25], allocatedBytes: 0);

        Assert.Equal(0.25, result.P50Milliseconds, 6);
        Assert.Equal(0.25, result.P95Milliseconds, 6);
        Assert.Equal(0.25, result.MaxMilliseconds, 6);
    }

    [Fact]
    public void TickBenchmark_Measure_AdvancesSimulationAndReturnsSamples()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());

        TickBenchmarkResult result = TickBenchmark.Measure(sim, 20);

        Assert.Equal(20, result.Ticks);
        Assert.Equal(2.0, sim.SimTime, 6);
        Assert.True(result.TotalMilliseconds > 0.0);
        Assert.True(result.MeanMilliseconds > 0.0);
        Assert.True(result.MaxMilliseconds >= result.P95Milliseconds);
        Assert.True(result.P95Milliseconds >= result.P50Milliseconds);
        Assert.True(result.AllocatedBytes >= 0);
    }

    [Fact]
    public void TickBenchmark_Measure_RejectsNonPositiveTickCount()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());

        Assert.Throws<ArgumentOutOfRangeException>(() => TickBenchmark.Measure(sim, 0));
    }

    [Fact]
    public void CrashReport_ContainsVersionExceptionDetailsAndRecentEvents()
    {
        var exception = new InvalidOperationException(
            "Simulation exploded.",
            new ArgumentException("inner cause"));

        string report = CrashReporting.BuildReport(
            exception,
            DateTimeOffset.Parse("2026-11-09T12:00:00Z"),
            "Voidworks.Game.dll --headless",
            ["[T+1.0s] JobFailed: weld the breach", "[T+2.0s] CrewDied: Miller"]);

        Assert.Contains($"Voidworks {BuildInfo.Version} crash report", report);
        Assert.Contains("2026-11-09T12:00:00Z", report);
        Assert.Contains("InvalidOperationException: Simulation exploded.", report);
        Assert.Contains("ArgumentException: inner cause", report);
        Assert.Contains("Voidworks.Game.dll --headless", report);
        Assert.Contains("Recent simulation events (last 2)", report);
        Assert.Contains("JobFailed: weld the breach", report);
        Assert.Contains("CrewDied: Miller", report);
        Assert.Contains("OS:", report);
        Assert.Contains("Runtime:", report);
    }

    [Fact]
    public void CrashReport_WriteReport_CreatesReadableLogFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "voidworks-crash-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string path = CrashReporting.WriteReport(
                new Exception("write test"),
                logDirectory: directory,
                commandLine: "test-command",
                recentEvents: ["[T+0.0s] JobPosted: test"]);

            Assert.True(File.Exists(path));
            Assert.StartsWith(directory, path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("crash-", Path.GetFileName(path));

            string content = File.ReadAllText(path);
            Assert.Contains("write test", content);
            Assert.Contains("test-command", content);
            Assert.Contains("JobPosted: test", content);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CrashReport_TryWrite_ReturnsNullWhenDirectoryCannotBeCreated()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"voidworks-blocker-{Guid.NewGuid():N}.txt");
        File.WriteAllText(filePath, "not a directory");
        try
        {
            string? path = CrashReporting.TryWrite(
                new Exception("boom"),
                logDirectory: filePath);

            Assert.Null(path);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
