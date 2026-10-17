using System;
using System.Collections.Generic;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Power;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class SimulationState
{
    public ShipGrid Grid { get; }
    public AtmosphereSolver AtmosphereSolver { get; }
    public PowerGridSolver PowerGridSolver { get; } = new();
    public JobManager JobManager { get; }
    public List<CrewAI> Crew { get; } = new();
    public List<Machine> Machines { get; } = new();
    public HashSet<GridCoord> Cables { get; } = new();

    public double SimTime { get; private set; }
    public bool IsPaused { get; set; } = false;
    public double TimeScale { get; set; } = 1.0; // 1x, 2x, 5x, 10x

    public const double FixedTickDt = 0.1; // 10 fixed ticks per simulation second
    private double _accumulator = 0.0;

    public SimulationState(ShipGrid grid, AtmosphereSolver solver, JobManager jobManager)
    {
        Grid = grid;
        AtmosphereSolver = solver;
        JobManager = jobManager;
    }

    public void Step(double dt)
    {
        // 1. Solve power grid
        PowerGridSolver.Tick(Grid, Machines, Cables, dt);

        // 2. Tick machines (life support, generators)
        foreach (var m in Machines)
        {
            if (Grid.IsInBounds(m.Position))
            {
                m.Tick(Grid[m.Position], dt);
            }
        }

        // 3. Solve atmosphere flow & venting
        AtmosphereSolver.Tick(Grid, dt);

        // 4. Tick crew physiology & autonomous AI
        foreach (var c in Crew)
        {
            c.Tick(Grid, JobManager, dt);
        }

        // 5. Detect world emergencies (breaches, broken systems)
        JobManager.ScanWorldForEmergencies(Grid);

        SimTime += dt;
    }

    public void Update(double realDeltaTime)
    {
        if (IsPaused || realDeltaTime <= 0) return;

        double clampedDelta = Math.Min(realDeltaTime, 0.25);
        _accumulator += clampedDelta * TimeScale;

        while (_accumulator >= FixedTickDt)
        {
            Step(FixedTickDt);
            _accumulator -= FixedTickDt;
        }
    }
}
