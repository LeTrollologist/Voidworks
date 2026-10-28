using System;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class SimulationTimingTests
{
    private static SimulationState CreateSim()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);
        return new SimulationState(grid, new AtmosphereSolver(), new JobManager());
    }

    [Fact]
    public void Pause_DoesNotAdvanceSimulation()
    {
        var sim = CreateSim();
        sim.IsPaused = true;

        sim.Update(1.0);
        sim.Update(5.0);

        Assert.Equal(0.0, sim.SimTime);
        Assert.Empty(sim.JobManager.ActiveJobs);
    }

    [Fact]
    public void TimeScale_RunsExpectedFixedTicks()
    {
        var sim = CreateSim();
        sim.TimeScale = 2.0;

        // 0.25 s is within the frame-stall clamp; at 2x it advances 0.5 s of sim.
        sim.Update(0.25);

        Assert.Equal(0.5, sim.SimTime, precision: 8);
    }

    [Fact]
    public void Update_AccumulatesPartialTicks_WithoutLosingRemainder()
    {
        var sim = CreateSim();

        sim.Update(0.05);
        Assert.Equal(0.0, sim.SimTime);

        sim.Update(0.05);
        Assert.Equal(0.1, sim.SimTime, precision: 8);

        sim.Update(0.04);
        Assert.Equal(0.1, sim.SimTime, precision: 8);
    }

    [Fact]
    public void FrameStall_IsClampedToQuarterSecond()
    {
        var sim = CreateSim();

        sim.Update(10.0);

        // The 0.25 s clamp allows only two fixed ticks (0.2 s) per update.
        Assert.Equal(0.2, sim.SimTime, precision: 8);
    }

    [Fact]
    public void HeadlessStep_ProducesExactTickCount()
    {
        var sim = CreateSim();

        for (int tick = 0; tick < 1000; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.Equal(100.0, sim.SimTime, precision: 6);
        Assert.Equal(10, (int)Math.Round(1.0 / SimulationState.FixedTickDt));
    }
}
