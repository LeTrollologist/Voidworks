using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Power;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim;

public sealed class SimulationState : IJobWorldContext
{
    public ShipGrid Grid { get; }
    public AtmosphereSolver AtmosphereSolver { get; }
    public PowerGridSolver PowerGridSolver { get; } = new();
    public PlumbingGridSolver PlumbingSolver { get; } = new();
    public JobManager JobManager { get; }
    public List<CrewAI> Crew { get; } = new();
    IReadOnlyList<CrewAI> IJobWorldContext.Crew => Crew;
    public List<Machine> Machines { get; } = new();
    public HashSet<GridCoord> Cables { get; } = new();
    public Scenario? ActiveScenario { get; set; }

    public double SimTime { get; private set; }
    public bool IsPaused { get; set; } = false;
    public double TimeScale { get; set; } = 1.0; // 1x, 2x, 5x, 10x

    /// <summary>
    /// Enables development invariant validation at the end of every tick. Tests and
    /// diagnostic runs enable this; normal play leaves it off to avoid the cost.
    /// </summary>
    public bool InvariantChecksEnabled { get; set; }

    public SimulationEventLog Events => JobManager.Events;

    private readonly Dictionary<Guid, CrewVitalSnapshot> _crewVitals = new();

    private readonly record struct CrewVitalSnapshot(bool IsAlive, bool IsIncapacitated);

    public IReadOnlyList<SimulationInvariantViolation> ValidateInvariants()
        => SimulationInvariants.Validate(this);

    /// <summary>Recent player-facing alerts derived from the event history.</summary>
    public IReadOnlyList<SimulationAlert> GetAlerts(int capacity = SimulationAlertFeed.DefaultCapacity)
        => SimulationAlertFeed.Build(Events.Recent, capacity);

    /// <summary>
    /// Applies a validated player command between fixed ticks. Every outcome is
    /// returned to the caller and appended to the event history, so rejected
    /// actions are as observable as successful ones.
    /// </summary>
    public SimulationCommandResult ExecuteCommand(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        SimulationCommandResult result;
        try
        {
            result = command.Execute(this);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            result = SimulationCommandResult.Fail("CommandException", exception.Message);
        }

        string detail = result.Success
            ? $"{command.Name}: {result.Message}"
            : $"{command.Name} rejected ({result.FailureCode}): {result.Message}";
        Events.Add(
            SimTime,
            result.Success ? SimulationEventKind.CommandApplied : SimulationEventKind.CommandRejected,
            detail,
            result.JobId);
        return result;
    }

    public const double FixedTickDt = 0.1; // 10 fixed ticks per simulation second
    private double _accumulator = 0.0;
    internal double UpdateAccumulator => _accumulator;

    public SimulationState(ShipGrid grid, AtmosphereSolver solver, JobManager jobManager)
    {
        Grid = grid;
        AtmosphereSolver = solver;
        JobManager = jobManager;
        JobManager.BindWorld(this);
    }

    public void Step(double dt)
    {
        JobManager.AdvanceClock(SimTime);

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
            c.Tick(Grid, JobManager, dt, nearbyCrewCount, Crew, Machines);
        }

        // Crew that died or became incapacitated during this tick must not leave
        // jobs reserved. Release them before new emergencies are posted.
        foreach (var c in Crew.Where(c =>
                     !c.Crew.Physiology.IsAlive || c.Crew.Physiology.IsIncapacitated))
        {
            JobManager.ReleaseJobsAssignedTo(c.Crew.Id);
        }

        // 7. Detect world emergencies (breaches, broken systems)
        JobManager.ScanWorldForEmergencies(Grid, Machines);

        // 8. Update active scenario objectives and win/loss conditions
        ActiveScenario?.Update(this, dt);

        SimTime += dt;
        RecordCrewVitalChanges();
        ThrowIfInvariantViolations();
    }

    private void RecordCrewVitalChanges()
    {
        foreach (var agent in Crew)
        {
            Guid crewId = agent.Crew.Id;
            bool isAlive = agent.Crew.Physiology.IsAlive;
            bool isIncapacitated = agent.Crew.Physiology.IsIncapacitated;
            if (_crewVitals.TryGetValue(crewId, out CrewVitalSnapshot previous))
            {
                if (previous.IsAlive && !isAlive)
                {
                    Events.Add(SimTime, SimulationEventKind.CrewDied,
                        $"{agent.Crew.Name} died at {agent.Crew.Position}", crewId: crewId);
                }
                else if (previous.IsAlive && !previous.IsIncapacitated && isIncapacitated)
                {
                    Events.Add(SimTime, SimulationEventKind.CrewIncapacitated,
                        $"{agent.Crew.Name} became incapacitated at {agent.Crew.Position}", crewId: crewId);
                }
            }

            _crewVitals[crewId] = new CrewVitalSnapshot(isAlive, isIncapacitated);
        }
    }

    private void ThrowIfInvariantViolations()
    {
        if (!InvariantChecksEnabled)
            return;

        IReadOnlyList<SimulationInvariantViolation> violations = ValidateInvariants();
        if (violations.Count > 0)
            throw new SimulationInvariantException(violations);
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
        PostProcessorInputDeliveryJobs();
        PostOreDeliveryJobs();

        foreach (var m in Machines)
        {
            if (!m.IsOnline || !m.IsPowered) continue;

            // 1. Mining Laser: Pulses asteroid to extract ore
            if (m.Type == MachineType.MiningLaser)
            {
                if (!m.CanStartCycle(ProductionRecipe.MineOre))
                    continue;

                if (!m.TargetAsteroidCoord.HasValue || !Grid.IsInBounds(m.TargetAsteroidCoord.Value) || Grid[m.TargetAsteroidCoord.Value].Type != TileType.Asteroid)
                {
                    m.TargetAsteroidCoord = FindNearestAsteroid(m.Position, maxRange: m.MiningRange);
                }

                if (m.TargetAsteroidCoord.HasValue)
                {
                    m.MiningProgressSeconds += dt * m.PowerFraction;
                    if (m.MiningProgressSeconds >= m.MiningCycleSeconds)
                    {
                        if (JobManager.IsTileFull(m.Position))
                        {
                            m.MiningProgressSeconds = m.MiningCycleSeconds;
                            continue;
                        }

                        m.MiningProgressSeconds = 0.0;
                        var astTile = Grid[m.TargetAsteroidCoord.Value];

                        if (astTile.AsteroidIronOre > 0)
                        {
                            astTile.AsteroidIronOre--;
                            JobManager.AddItem(new Item(ItemType.IronOre, m.Position));
                            m.TotalOreExtracted++;
                            m.CompleteCycle(ProductionRecipe.MineOre);
                        }
                        else if (astTile.AsteroidIceOre > 0)
                        {
                            astTile.AsteroidIceOre--;
                            JobManager.AddItem(new Item(ItemType.IceOre, m.Position));
                            m.TotalOreExtracted++;
                            m.CompleteCycle(ProductionRecipe.MineOre);
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

                if (m.SteelPlatesProduced > 0 && !JobManager.IsTileFull(m.Position))
                {
                    m.SteelPlatesProduced--;
                    JobManager.AddItem(new Item(ItemType.SteelPlate, m.Position));
                }
            }

            // 3. Hydroponics Bay: Ejects finished food rations and accepts water rations
            else if (m.Type == MachineType.HydroponicsBay)
            {
                if (m.WaterStoredLiters <= 10.0)
                {
                    var waterOnTile = JobManager.WorldItems.FirstOrDefault(i =>
                        i.Position == m.Position && i.Type == ItemType.WaterRation && i.IsAvailable);
                    if (waterOnTile != null)
                    {
                        JobManager.RemoveItem(waterOnTile);
                        m.WaterStoredLiters += 5.0;
                    }
                }

                if (m.FoodRationsProduced > 0 && !JobManager.IsTileFull(m.Position))
                {
                    m.FoodRationsProduced--;
                    JobManager.AddItem(new Item(ItemType.FoodRation, m.Position));
                }
            }
            else if (m.Type == MachineType.FuelTank)
            {
                if (m.FuelStoredKg < m.FuelCapacityKg)
                {
                    var fuelOnTile = JobManager.WorldItems.FirstOrDefault(i =>
                        i.Position == m.Position && i.Type == ItemType.FuelCanister && i.IsAvailable);
                    if (fuelOnTile != null)
                    {
                        JobManager.RemoveItem(fuelOnTile);
                        m.FuelStoredKg = Math.Min(m.FuelCapacityKg, m.FuelStoredKg + fuelOnTile.WeightKg);
                    }
                }
            }
            else if (m.Type == MachineType.ComponentFabricator)
            {
                var steel = JobManager.WorldItems
                    .Where(item => item.Position == m.Position && item.Type == ItemType.SteelPlate && item.IsAvailable)
                    .Take(2).ToList();
                var iron = JobManager.WorldItems.FirstOrDefault(item =>
                    item.Position == m.Position && item.Type == ItemType.IronOre && item.IsAvailable);

                if (m.ComponentsProduced > 0)
                {
                    if (JobManager.IsTileFull(m.Position))
                        continue;

                    m.ComponentsProduced--;
                    JobManager.AddItem(new Item(ItemType.MachineComponent, m.Position));
                }
                else
                {
                    if (!m.CanProduce)
                        continue;

                    if (!m.IsComponentFabricationInProgress && steel.Count == 2 && iron != null &&
                        m.CanStartCycle(ProductionRecipe.FabricateComponent))
                    {
                        foreach (var plate in steel) JobManager.RemoveItem(plate);
                        JobManager.RemoveItem(iron);
                        m.IsComponentFabricationInProgress = true;
                    }

                    if (m.IsComponentFabricationInProgress)
                    {
                        m.ComponentFabricationProgressSeconds += dt * m.PowerFraction;
                        if (m.ComponentFabricationProgressSeconds >= m.ComponentFabricationCycleSeconds)
                        {
                            m.ComponentFabricationProgressSeconds -= m.ComponentFabricationCycleSeconds;
                            m.ComponentsProduced++;
                            m.CompleteCycle(ProductionRecipe.FabricateComponent);
                            m.IsComponentFabricationInProgress = false;
                        }
                    }
                }
            }
            else if (m.Type == MachineType.IceProcessor)
            {
                ProductionRecipe desiredRecipe = m.ProductionQueue.Count > 0
                    ? m.ProductionQueue[0].Recipe
                    : m.ProduceFuelCanisters ? ProductionRecipe.ProcessFuel : ProductionRecipe.ProcessWater;
                if (desiredRecipe is not (ProductionRecipe.ProcessFuel or ProductionRecipe.ProcessWater))
                    continue;

                if (desiredRecipe == ProductionRecipe.ProcessFuel)
                {
                    if (m.FuelCanistersProduced > 0)
                    {
                        if (JobManager.IsTileFull(m.Position))
                            continue;

                        m.FuelCanistersProduced--;
                        JobManager.AddItem(new Item(ItemType.FuelCanister, m.Position));
                    }
                    else
                    {
                        if (!m.CanProduce || !m.CanStartCycle(desiredRecipe))
                            continue;

                        if (!m.IsIceProcessingInProgress)
                        {
                            var iceChunks = JobManager.WorldItems
                                .Where(item => item.Position == m.Position && item.Type == ItemType.IceOre && item.IsAvailable)
                                .Take(2).ToList();
                            if (iceChunks.Count == 2)
                            {
                                foreach (var chunk in iceChunks) JobManager.RemoveItem(chunk);
                                m.IsIceProcessingInProgress = true;
                            }
                        }

                        if (m.IsIceProcessingInProgress)
                        {
                            m.IceProcessingProgressSeconds += dt * m.PowerFraction;
                            if (m.IceProcessingProgressSeconds >= m.IceProcessingCycleSeconds)
                            {
                                m.IceProcessingProgressSeconds -= m.IceProcessingCycleSeconds;
                                m.FuelCanistersProduced++;
                                m.CompleteCycle(ProductionRecipe.ProcessFuel);
                                m.IsIceProcessingInProgress = false;
                            }
                        }
                    }
                }
                else if (desiredRecipe == ProductionRecipe.ProcessWater)
                {
                    if (m.WaterRationsProduced > 0)
                    {
                        if (JobManager.IsTileFull(m.Position))
                            continue;

                        m.WaterRationsProduced--;
                        JobManager.AddItem(new Item(ItemType.WaterRation, m.Position));
                    }
                    else
                    {
                        if (!m.CanProduce || !m.CanStartCycle(desiredRecipe))
                            continue;

                        if (!m.IsIceProcessingInProgress)
                        {
                            var ice = JobManager.WorldItems.FirstOrDefault(item =>
                                item.Position == m.Position && item.Type == ItemType.IceOre && item.IsAvailable);
                            if (ice != null)
                            {
                                JobManager.RemoveItem(ice);
                                m.IsIceProcessingInProgress = true;
                            }
                        }

                        if (m.IsIceProcessingInProgress)
                        {
                            m.IceProcessingProgressSeconds += dt * m.PowerFraction;
                            if (m.IceProcessingProgressSeconds >= m.IceProcessingCycleSeconds)
                            {
                                m.IceProcessingProgressSeconds -= m.IceProcessingCycleSeconds;
                                m.WaterRationsProduced++;
                                m.CompleteCycle(ProductionRecipe.ProcessWater);
                                m.IsIceProcessingInProgress = false;
                            }
                        }
                    }
                }
            }
        }
    }

    private void PostProcessorInputDeliveryJobs()
    {
        foreach (var machine in Machines.Where(machine => machine.IsOnline &&
                     machine.Type is MachineType.ComponentFabricator or MachineType.IceProcessor))
        {
            if (JobManager.IsTileFull(machine.Position))
                continue;

            ItemType inputType = machine.Type == MachineType.ComponentFabricator
                ? ItemType.SteelPlate
                : ItemType.IceOre;
            int targetStock = 2;
            int onSite = JobManager.WorldItems.Count(item =>
                item.Position == machine.Position && item.Type == inputType && item.IsAvailable);
            int inTransit = JobManager.ActiveJobs.Count(job => job.Type == JobType.DeliverItem &&
                job.TargetCoord == machine.Position && job.DesiredItemType == inputType);
            int alreadyAvailable = onSite + inTransit;
            int needed = Math.Max(0, targetStock - alreadyAvailable);

            for (int i = 0; i < needed; i++)
            {
                var item = JobManager.WorldItems
                    .Where(candidate => candidate.Type == inputType && candidate.IsAvailable &&
                        !JobManager.ActiveJobs.Any(job => job.TargetItem?.Id == candidate.Id))
                    .OrderBy(candidate => Math.Abs(candidate.Position.X - machine.Position.X) +
                                          Math.Abs(candidate.Position.Y - machine.Position.Y))
                    .FirstOrDefault();
                if (item == null) break;
                JobManager.PostJob(Job.CreateDeliverItem(machine.Position, item, priority: 45));
            }

            if (machine.Type != MachineType.ComponentFabricator) continue;

            int ironOnSite = JobManager.WorldItems.Count(item =>
                item.Position == machine.Position && item.Type == ItemType.IronOre && item.IsAvailable);
            int ironInTransit = JobManager.ActiveJobs.Count(job => job.Type == JobType.DeliverItem &&
                job.TargetCoord == machine.Position && job.DesiredItemType == ItemType.IronOre);
            if (ironOnSite + ironInTransit >= 1) continue;

            var iron = JobManager.WorldItems
                .Where(item => item.Type == ItemType.IronOre && item.IsAvailable &&
                    !JobManager.ActiveJobs.Any(job => job.TargetItem?.Id == item.Id))
                .OrderBy(item => Math.Abs(item.Position.X - machine.Position.X) +
                                 Math.Abs(item.Position.Y - machine.Position.Y))
                .FirstOrDefault();
            if (iron != null)
                JobManager.PostJob(Job.CreateDeliverItem(machine.Position, iron, priority: 45));
        }

        foreach (var bay in Machines.Where(m => m.IsOnline && m.Type == MachineType.HydroponicsBay && m.WaterStoredLiters <= 5.0))
        {
            if (JobManager.IsTileFull(bay.Position))
                continue;

            int onSite = JobManager.WorldItems.Count(item =>
                item.Position == bay.Position && item.Type == ItemType.WaterRation && item.IsAvailable);
            int inTransit = JobManager.ActiveJobs.Count(job => job.Type == JobType.DeliverItem &&
                job.TargetCoord == bay.Position && job.DesiredItemType == ItemType.WaterRation);
            if (onSite + inTransit < 1)
            {
                var water = JobManager.WorldItems
                    .Where(item => item.Type == ItemType.WaterRation && item.IsAvailable &&
                        !JobManager.ActiveJobs.Any(job => job.TargetItem?.Id == item.Id))
                    .OrderBy(item => Math.Abs(item.Position.X - bay.Position.X) +
                                     Math.Abs(item.Position.Y - bay.Position.Y))
                    .FirstOrDefault();
                if (water != null)
                    JobManager.PostJob(Job.CreateDeliverItem(bay.Position, water, priority: 60));
            }
        }

        foreach (var tank in Machines.Where(m => m.IsOnline && m.Type == MachineType.FuelTank && m.FuelStoredKg < m.FuelCapacityKg))
        {
            if (JobManager.IsTileFull(tank.Position))
                continue;

            int onSite = JobManager.WorldItems.Count(item =>
                item.Position == tank.Position && item.Type == ItemType.FuelCanister && item.IsAvailable);
            int inTransit = JobManager.ActiveJobs.Count(job => job.Type == JobType.DeliverItem &&
                job.TargetCoord == tank.Position && job.DesiredItemType == ItemType.FuelCanister);
            if (onSite + inTransit < 1)
            {
                var fuel = JobManager.WorldItems
                    .Where(item => item.Type == ItemType.FuelCanister && item.IsAvailable &&
                        !JobManager.ActiveJobs.Any(job => job.TargetItem?.Id == item.Id))
                    .OrderBy(item => Math.Abs(item.Position.X - tank.Position.X) +
                                     Math.Abs(item.Position.Y - tank.Position.Y))
                    .FirstOrDefault();
                if (fuel != null)
                    JobManager.PostJob(Job.CreateDeliverItem(tank.Position, fuel, priority: 55));
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
                    job.TargetItem?.Id == item.Id));

            if (ore != null)
                JobManager.PostJob(Job.CreateRefineOre(smelter.Position, ore, priority: 45));
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
