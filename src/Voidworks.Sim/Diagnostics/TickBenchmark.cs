using System;
using System.Collections.Generic;
using System.Linq;

namespace Voidworks.Sim.Diagnostics;

/// <summary>Measured cost of advancing a simulation by fixed ticks.</summary>
public sealed record TickBenchmarkResult(
    int Ticks,
    double TotalMilliseconds,
    double MeanMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double MaxMilliseconds,
    long AllocatedBytes)
{
    public double SimulatedSeconds => Ticks * SimulationState.FixedTickDt;
    public double MillisecondsPerSimulatedSecond =>
        SimulatedSeconds <= 0.0 ? 0.0 : TotalMilliseconds / SimulatedSeconds;
    public double AllocatedKilobytesPerTick =>
        Ticks <= 0 ? 0.0 : AllocatedBytes / 1024.0 / Ticks;
}

/// <summary>
/// Headless tick profiler used by <c>--benchmark</c>. It measures the simulation
/// only; rendering is intentionally outside this measurement.
/// </summary>
public static class TickBenchmark
{
    public static TickBenchmarkResult Measure(SimulationState sim, int ticks)
    {
        ArgumentNullException.ThrowIfNull(sim);
        if (ticks <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Tick count must be positive.");

        var samples = new double[ticks];
        var stopwatch = new System.Diagnostics.Stopwatch();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int tick = 0; tick < ticks; tick++)
        {
            stopwatch.Restart();
            sim.Step(SimulationState.FixedTickDt);
            stopwatch.Stop();
            samples[tick] = stopwatch.Elapsed.TotalMilliseconds;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return Summarize(samples, allocated);
    }

    /// <summary>Builds a result from arbitrary samples; exposed for deterministic tests.</summary>
    public static TickBenchmarkResult Summarize(IReadOnlyList<double> samples, long allocatedBytes)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            throw new ArgumentException("At least one timing sample is required.", nameof(samples));

        double[] sorted = samples.OrderBy(value => value).ToArray();
        double total = samples.Sum();
        return new TickBenchmarkResult(
            samples.Count,
            total,
            total / samples.Count,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            sorted[^1],
            allocatedBytes);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
