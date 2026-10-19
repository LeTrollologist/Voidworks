using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Power;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim;

public sealed class SimulationState
{
    public ShipGrid Grid { get; }
    public AtmosphereSolver AtmosphereSolver { get; }
    public PowerGridSolver PowerGridSolver { get; } = new();
    public PlumbingGridSolver PlumbingSolver { get; } = new();
    public JobManager JobManager { get; }
    public List<CrewAI> Crew { get; } = new();
    public List<Machine> Machines { get; } = new();
    public HashSet<GridCoord> Cables { get; } = new();
    public Scenario? ActiveScenario { get; set; }

    public double SimTime { get; private set; }
    public bool IsPaused { get; set; } = false;
    public double TimeScale { get; set; } = 1.0; // 1x, 2x, 5x, 10x

    public const double FixedTickDt = 0.1; // 10 fixed ticks per simulation second
    private double _accumulator = 0.0;
    internal double UpdateAccumulator => _accumulator;

    public SimulationState(ShipGrid grid, AtmosphereSolver solver, JobManager jobManager)
    {
        Grid = grid;
        AtmosphereSolver = solver;
        JobManager = jobManager;
    }

    public void Step(double dt)
    {
        // 1. Solve electrical power grid
        PowerGridSolver.Tick(Grid, Machines, Cables, dt);

        // 2. Solve plumbing (coolant loop & propellant flow)
        PlumbingSolver.Tick(Grid, Machines, dt);

        // 3. Tick machines (generators, life support, industry)
        foreach (var m in Machines)
        {
            if (Grid.IsInBounds(m.Position))
            {
                m.Tick(Grid[m.Position], dt);
            }
        }

        // 4. Tick industry logistics (Mining Laser extraction, Smelter items, Hydroponics food)
        TickIndustrialLogistics(dt);

        // 5. Solve atmosphere flow & venting
        AtmosphereSolver.Tick(Grid, dt);

        // 6. Tick crew physiology & autonomous AI
        foreach (var c in Crew)
        {
            int nearbyCrewCount = Crew.Count(other => other != c &&
                other.Crew.Physiology.IsAlive && !other.Crew.Physiology.IsIncapacitated &&
                Math.Abs(other.Crew.Position.X - c.Crew.Position.X) +
                Math.Abs(other.Crew.Position.Y - c.Crew.Position.Y) <= 2);
            c.Tick(Grid, JobManager, dt, nearbyCrewCount);
        }

        // 7. Detect world emergencies (breaches, broken systems)
        JobManager.ScanWorldForEmergencies(Grid);

        // 8. Update active scenario objectives and win/loss conditions
        ActiveScenario?.Update(this, dt);

        SimTime += dt;
    }

    internal void RestoreClock(double simTime, double updateAccumulator)
    {
        if (!double.IsFinite(simTime) || simTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(simTime));
        if (!double.IsFinite(updateAccumulator) || updateAccumulator < 0.0)
            throw new ArgumentOutOfRangeException(nameof(updateAccumulator));

        SimTime = simTime;
        _accumulator = updateAccumulator;
    }

    private void TickIndustrialLogistics(double dt)
    {
        PostOreDeliveryJobs();

        foreach (var m in Machines)
        {
            if (!m.IsOnline || !m.IsPowered) continue;

            // 1. Mining Laser: Pulses asteroid to extract ore
            if (m.Type == MachineType.MiningLaser)
            {
                if (!m.TargetAsteroidCoord.HasValue || !Grid.IsInBounds(m.TargetAsteroidCoord.Value) || Grid[m.TargetAsteroidCoord.Value].Type != TileType.Asteroid)
                {
                    m.TargetAsteroidCoord = FindNearestAsteroid(m.Position, maxRange: 6);
                }

                if (m.TargetAsteroidCoord.HasValue)
                {
                    m.MiningProgressSeconds += dt;
                    if (m.MiningProgressSeconds >= m.MiningCycleSeconds)
                    {
                        m.MiningProgressSeconds = 0.0;
                        var astTile = Grid[m.TargetAsteroidCoord.Value];

                        if (astTile.AsteroidIronOre > 0)
                        {
                            astTile.AsteroidIronOre--;
                            JobManager.AddItem(new Item(ItemType.IronOre, m.Position));
                            m.TotalOreExtracted++;
                        }
                        else if (astTile.AsteroidIceOre > 0)
                        {
                            astTile.AsteroidIceOre--;
                            JobManager.AddItem(new Item(ItemType.IceOre, m.Position));
                            m.TotalOreExtracted++;
                        }

                        if (astTile.AsteroidIronOre <= 0 && astTile.AsteroidIceOre <= 0)
                        {
                            astTile.Type = TileType.Space; // asteroid depleted!
                            m.TargetAsteroidCoord = null;
                        }
                    }
                }
            }

            // 2. Smelter Refinery: Pulls raw ore on tile and ejects steel plates
            else if (m.Type == MachineType.SmelterRefinery)
            {
                if (m.IronOreStored < 5)
                {
                    var oreOnTile = JobManager.WorldItems.FirstOrDefault(i => i.Position == m.Position && i.Type == ItemType.IronOre && !i.IsCarried);
                    if (oreOnTile != null)
                    {
                        JobManager.RemoveItem(oreOnTile);
                        m.IronOreStored++;
                    }
                }

                if (m.SteelPlatesProduced > 0)
                {
                    m.SteelPlatesProduced--;
                    JobManager.AddItem(new Item(ItemType.SteelPlate, m.Position));
                }
            }

            // 3. Hydroponics Bay: Ejects finished food rations
            else if (m.Type == MachineType.HydroponicsBay)
            {
                if (m.FoodRationsProduced > 0)
                {
                    m.FoodRationsProduced--;
                    JobManager.AddItem(new Item(ItemType.FoodRation, m.Position));
                }
            }
        }
    }

    private void PostOreDeliveryJobs()
    {
        var smelters = Machines.Where(m => m.Type == MachineType.SmelterRefinery && m.IsOnline).ToList();
        foreach (var smelter in smelters)
        {
            var ore = JobManager.WorldItems
                .Where(item => item.Type == ItemType.IronOre && item.IsAvailable)
                .OrderBy(item => Math.Abs(item.Position.X - smelter.Position.X) + Math.Abs(item.Position.Y - smelter.Position.Y))
                .FirstOrDefault(item => !JobManager.ActiveJobs.Any(job =>
                    job.Type == JobType.RefineOre && job.TargetItem?.Id == item.Id));

            if (ore != null)
                JobManager.PostJob(Job.CreateRefineOre(smelter.Position, ore));
        }
    }

    private GridCoord? FindNearestAsteroid(GridCoord origin, int maxRange)
    {
        GridCoord? best = null;
        int bestDist = int.MaxValue;

        for (int y = 0; y < Grid.Height; y++)
        {
            for (int x = 0; x < Grid.Width; x++)
            {
                if (Grid[x, y].Type == TileType.Asteroid)
                {
                    int dist = Math.Abs(x - origin.X) + Math.Abs(y - origin.Y);
                    if (dist <= maxRange && dist < bestDist)
                    {
                        bestDist = dist;
                        best = new GridCoord(x, y);
                    }
                }
            }
        }

        return best;
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
