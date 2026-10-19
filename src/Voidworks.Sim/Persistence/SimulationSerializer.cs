using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Scenarios;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Persistence;

public static class SimulationSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void SaveToFile(SimulationState sim, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, SaveToJson(sim));
    }

    public static SimulationState LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadFromJson(File.ReadAllText(path));
    }

    public static string SaveToJson(SimulationState sim)
    {
        var dto = new SimulationSaveDto
        {
            SimTime = sim.SimTime,
            UpdateAccumulator = sim.UpdateAccumulator,
            CableCapacityKW = sim.PowerGridSolver.CableCapacityKW,
            CoolantCapacityLiters = sim.PlumbingSolver.CoolantCapacityLiters,
            CoolantStoredLiters = sim.PlumbingSolver.CoolantStoredLiters,
            CoolantReserveLiters = sim.PlumbingSolver.CoolantReserveLiters,
            CoolantLeakRateLitersPerSecond = sim.PlumbingSolver.CoolantLeakRateLitersPerSecond,
            PropellantPipeCapacityKgPerSecond = sim.PlumbingSolver.PropellantPipeCapacityKgPerSecond,
            IsPaused = sim.IsPaused,
            TimeScale = sim.TimeScale,
            GridWidth = sim.Grid.Width,
            GridHeight = sim.Grid.Height,
            Tiles = sim.Grid.AllTiles().Select(t => new TileSaveDto
            {
                X = t.Coord.X,
                Y = t.Coord.Y,
                Type = t.Type,
                DoorState = t.DoorState,
                IsBreached = t.IsBreached,
                O2 = t.Atmosphere.Oxygen,
                N2 = t.Atmosphere.Nitrogen,
                CO2 = t.Atmosphere.CarbonDioxide,
                ToxicGas = t.Atmosphere.ToxicGas,
                TempK = t.Atmosphere.TemperatureK,
                Volume = t.Atmosphere.Volume,
                IronOre = t.AsteroidIronOre,
                IceOre = t.AsteroidIceOre
            }).ToList(),
            Machines = sim.Machines.Select(m => new MachineSaveDto
            {
                Id = m.Id,
                Name = m.Name,
                Type = m.Type,
                X = m.Position.X,
                Y = m.Position.Y,
                IsOnline = m.IsOnline,
                IsPowered = m.IsPowered,
                PowerOutput = m.PowerProductionKW,
                RatedPowerOutput = m.RatedPowerProductionKW,
                PowerDemand = m.PowerConsumptionKW,
                BatteryMaxCharge = m.BatteryMaxChargeKW,
                BatteryMaxDischarge = m.BatteryMaxDischargeKW,
                O2GenerationRate = m.O2GenerationRatePerSecond,
                CO2ScrubbingRate = m.CO2ScrubbingRatePerSecond,
                CoreTempK = m.CoreTemperatureKelvin,
                HeatGeneration = m.HeatGenerationKW,
                HeatCapacity = m.HeatCapacityJoulesPerKelvin,
                MaxSafeTempK = m.MaxSafeTemperatureKelvin,
                ScramTempK = m.ScramTemperatureKelvin,
                ScramResetTempK = m.ScramResetTemperatureKelvin,
                IsScrammed = m.IsScrammed,
                RadiatorCooling = m.RadiatorCoolingKW,
                CoolantPumpFlowRate = m.CoolantPumpFlowRateLitersPerSecond,
                BatteryStored = m.BatteryStoredKWh,
                BatteryCapacity = m.BatteryCapacityKWh,
                TargetAsteroidX = m.TargetAsteroidCoord?.X,
                TargetAsteroidY = m.TargetAsteroidCoord?.Y,
                MiningProgress = m.MiningProgressSeconds,
                MiningCycle = m.MiningCycleSeconds,
                IronOreStored = m.IronOreStored,
                SteelPlatesProduced = m.SteelPlatesProduced,
                SmeltProgress = m.SmeltProgressSeconds,
                SmeltCycle = m.SmeltCycleSeconds,
                WaterStored = m.WaterStoredLiters,
                FoodProduced = m.FoodRationsProduced,
                GrowthProgress = m.GrowthProgressSeconds,
                GrowthCycle = m.GrowthCycleSeconds,
                FuelStored = m.FuelStoredKg,
                FuelCapacity = m.FuelCapacityKg,
                IsFiring = m.IsFiring,
                Thrust = m.ThrustKN,
                FuelBurnRate = m.FuelBurnRateKgPerSecond,
                TotalOreExtracted = m.TotalOreExtracted
            }).ToList(),
            Cables = sim.Cables.Select(c => new CoordDto { X = c.X, Y = c.Y }).ToList(),
            CoolantPipes = sim.PlumbingSolver.CoolantPipes.Select(c => new CoordDto { X = c.X, Y = c.Y }).ToList(),
            PropellantPipes = sim.PlumbingSolver.PropellantPipes.Select(c => new CoordDto { X = c.X, Y = c.Y }).ToList(),
            WorldItems = sim.JobManager.WorldItems.Where(i => !i.IsCarried).Select(ToItemDto).ToList(),
            Crew = sim.Crew.Select(c => new CrewSaveDto
            {
                Id = c.Crew.Id,
                Name = c.Crew.Name,
                Role = c.Crew.Role,
                X = c.Crew.Position.X,
                Y = c.Crew.Position.Y,
                MoveSpeed = c.Crew.MoveSpeed,
                IsEquippedWithSuit = c.Crew.IsEquippedWithSuit,
                CurrentActivity = c.CurrentActivity,
                CurrentJob = c.CurrentJob != null ? ToJobDto(c.CurrentJob) : null,
                PendingSubJobs = c.PendingSubJobs.Select(ToJobDto).ToList(),
                CurrentPath = c.CurrentPath.Select(ToCoordDto).ToList(),
                MovementAccumulator = c.MovementAccumulator,
                BunkX = c.Crew.AssignedBunkCoord?.X,
                BunkY = c.Crew.AssignedBunkCoord?.Y,
                Health = c.Crew.Physiology.Health,
                O2Sat = c.Crew.Physiology.OxygenSaturation,
                Hunger = c.Crew.Physiology.Hunger,
                Thirst = c.Crew.Physiology.Thirst,
                Fatigue = c.Crew.Physiology.Fatigue,
                Courage = c.Crew.Psychology.Courage,
                Discipline = c.Crew.Psychology.Discipline,
                SelfPreservation = c.Crew.Psychology.SelfPreservation,
                Determination = c.Crew.Psychology.Determination,
                Sociability = c.Crew.Psychology.Sociability,
                Fear = c.Crew.Psychology.Fear,
                Stress = c.Crew.Psychology.Stress,
                Anger = c.Crew.Psychology.Anger,
                Motivation = c.Crew.Psychology.Motivation,
                Morale = c.Crew.Psychology.Morale,
                Inventory = c.Crew.Inventory.Select(ToItemDto).ToList(),
                EquippedTank = c.Crew.EquippedOxygenTank != null ? ToItemDto(c.Crew.EquippedOxygenTank) : null
            }).ToList(),
            Jobs = sim.JobManager.ActiveJobs.Select(ToJobDto).ToList(),
            ScenarioId = sim.ActiveScenario?.Id,
            ScenarioTimeLimit = sim.ActiveScenario?.TimeLimitSeconds,
            ScenarioElapsed = sim.ActiveScenario?.ElapsedSeconds,
            ScenarioCompleted = sim.ActiveScenario?.IsCompleted ?? false,
            ScenarioFailed = sim.ActiveScenario?.IsFailed ?? false,
            ScenarioFailureReason = sim.ActiveScenario?.FailureReason,
            ScenarioObjectives = sim.ActiveScenario?.Objectives.Select(o => new ScenarioObjectiveSaveDto
            {
                Id = o.Id,
                IsCompleted = o.IsCompleted,
                IsFailed = o.IsFailed
            }).ToList() ?? new List<ScenarioObjectiveSaveDto>()
        };

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static SimulationState LoadFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<SimulationSaveDto>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize simulation save data.");

        var grid = new ShipGrid(dto.GridWidth, dto.GridHeight);
        foreach (var tDto in dto.Tiles)
        {
            var tile = grid[tDto.X, tDto.Y];
            tile.Type = tDto.Type;
            tile.DoorState = tDto.DoorState;
            tile.IsBreached = tDto.IsBreached;
            tile.AsteroidIronOre = tDto.IronOre;
            tile.AsteroidIceOre = tDto.IceOre;
            tile.Atmosphere = new GasMix(tDto.Volume, tDto.TempK)
            {
                Oxygen = tDto.O2,
                Nitrogen = tDto.N2,
                CarbonDioxide = tDto.CO2,
                ToxicGas = tDto.ToxicGas
            };
        }

        var jobManager = new JobManager();
        var atmosphereSolver = new AtmosphereSolver();
        var sim = new SimulationState(grid, atmosphereSolver, jobManager);

        sim.IsPaused = dto.IsPaused;
        sim.TimeScale = dto.TimeScale;
        sim.RestoreClock(dto.SimTime, dto.UpdateAccumulator);
        sim.PowerGridSolver.CableCapacityKW = dto.CableCapacityKW;
        sim.PlumbingSolver.CoolantCapacityLiters = dto.CoolantCapacityLiters;
        sim.PlumbingSolver.CoolantStoredLiters = dto.CoolantStoredLiters;
        sim.PlumbingSolver.CoolantReserveLiters = dto.CoolantReserveLiters;
        sim.PlumbingSolver.CoolantLeakRateLitersPerSecond = dto.CoolantLeakRateLitersPerSecond;
        sim.PlumbingSolver.PropellantPipeCapacityKgPerSecond = dto.PropellantPipeCapacityKgPerSecond;

        // Restore Machines
        foreach (var mDto in dto.Machines)
        {
            var m = new Machine(mDto.Type, new GridCoord(mDto.X, mDto.Y), mDto.Name)
            {
                IsOnline = mDto.IsOnline,
                IsPowered = mDto.IsPowered,
                PowerProductionKW = mDto.PowerOutput,
                RatedPowerProductionKW = mDto.RatedPowerOutput,
                PowerConsumptionKW = mDto.PowerDemand,
                BatteryMaxChargeKW = mDto.BatteryMaxCharge,
                BatteryMaxDischargeKW = mDto.BatteryMaxDischarge,
                O2GenerationRatePerSecond = mDto.O2GenerationRate,
                CO2ScrubbingRatePerSecond = mDto.CO2ScrubbingRate,
                CoreTemperatureKelvin = mDto.CoreTempK,
                HeatGenerationKW = mDto.HeatGeneration,
                HeatCapacityJoulesPerKelvin = mDto.HeatCapacity,
                MaxSafeTemperatureKelvin = mDto.MaxSafeTempK,
                ScramTemperatureKelvin = mDto.ScramTempK,
                ScramResetTemperatureKelvin = mDto.ScramResetTempK,
                IsScrammed = mDto.IsScrammed,
                RadiatorCoolingKW = mDto.RadiatorCooling,
                CoolantPumpFlowRateLitersPerSecond = mDto.CoolantPumpFlowRate,
                BatteryStoredKWh = mDto.BatteryStored,
                BatteryCapacityKWh = mDto.BatteryCapacity,
                WaterStoredLiters = mDto.WaterStored,
                FoodRationsProduced = mDto.FoodProduced,
                IronOreStored = mDto.IronOreStored,
                SteelPlatesProduced = mDto.SteelPlatesProduced,
                SmeltProgressSeconds = mDto.SmeltProgress,
                SmeltCycleSeconds = mDto.SmeltCycle,
                GrowthProgressSeconds = mDto.GrowthProgress,
                GrowthCycleSeconds = mDto.GrowthCycle,
                FuelStoredKg = mDto.FuelStored,
                FuelCapacityKg = mDto.FuelCapacity,
                IsFiring = mDto.IsFiring,
                ThrustKN = mDto.Thrust,
                FuelBurnRateKgPerSecond = mDto.FuelBurnRate,
                MiningProgressSeconds = mDto.MiningProgress,
                MiningCycleSeconds = mDto.MiningCycle,
                TotalOreExtracted = mDto.TotalOreExtracted
            };
            if (mDto.TargetAsteroidX.HasValue && mDto.TargetAsteroidY.HasValue)
                m.TargetAsteroidCoord = new GridCoord(mDto.TargetAsteroidX.Value, mDto.TargetAsteroidY.Value);
            m.RestoreId(mDto.Id);
            sim.Machines.Add(m);
        }

        // Restore Utilities
        foreach (var c in dto.Cables) sim.Cables.Add(new GridCoord(c.X, c.Y));
        foreach (var c in dto.CoolantPipes) sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(c.X, c.Y));
        foreach (var c in dto.PropellantPipes) sim.PlumbingSolver.PropellantPipes.Add(new GridCoord(c.X, c.Y));

        // Restore items before jobs so all saved references resolve to one object.
        var itemsById = new Dictionary<Guid, Item>();
        foreach (var iDto in dto.WorldItems)
        {
            var item = FromItemDto(iDto);
            jobManager.AddItem(item);
            itemsById[item.Id] = item;
        }

        // Restore Crew
        foreach (var cDto in dto.Crew)
        {
            var crew = new CrewMember(cDto.Name, new GridCoord(cDto.X, cDto.Y), cDto.Role)
            {
                MoveSpeed = cDto.MoveSpeed,
                IsEquippedWithSuit = cDto.IsEquippedWithSuit
            };
            crew.RestoreId(cDto.Id);

            if (cDto.BunkX.HasValue && cDto.BunkY.HasValue)
            {
                crew.AssignedBunkCoord = new GridCoord(cDto.BunkX.Value, cDto.BunkY.Value);
            }

            crew.Physiology.Health = cDto.Health;
            crew.Physiology.OxygenSaturation = cDto.O2Sat;
            crew.Physiology.Hunger = cDto.Hunger;
            crew.Physiology.Thirst = cDto.Thirst;
            crew.Physiology.Fatigue = cDto.Fatigue;
            crew.Psychology.Restore(
                cDto.Courage,
                cDto.Discipline ?? cDto.WorkEthic ?? 50.0,
                cDto.SelfPreservation,
                cDto.Determination ?? 50.0,
                cDto.Sociability ?? 50.0,
                cDto.Motivation,
                cDto.Fear,
                cDto.Stress ?? 0.0,
                cDto.Anger,
                cDto.Morale ?? 70.0);

            foreach (var invItemDto in cDto.Inventory)
            {
                var invItem = FromItemDto(invItemDto);
                invItem.IsCarried = true;
                crew.Inventory.Add(invItem);
                itemsById[invItem.Id] = invItem;
            }

            if (cDto.EquippedTank != null)
            {
                var tank = FromItemDto(cDto.EquippedTank);
                tank.IsCarried = true;
                crew.EquippedOxygenTank = tank;
                itemsById[tank.Id] = tank;
            }

            sim.Crew.Add(new CrewAI(crew));
        }

        var jobsById = new Dictionary<Guid, Job>();
        foreach (var jobDto in dto.Jobs)
        {
            var job = FromJobDto(jobDto, itemsById);
            jobsById[job.Id] = job;
            jobManager.PostJob(job);
        }

        var crewAisById = sim.Crew.ToDictionary(c => c.Crew.Id);
        foreach (var crewDto in dto.Crew)
        {
            var ai = crewAisById[crewDto.Id];
            Job? currentJob = crewDto.CurrentJob != null
                ? ResolveJob(crewDto.CurrentJob, jobsById, itemsById)
                : null;
            var pendingJobs = crewDto.PendingSubJobs.Select(j => ResolveJob(j, jobsById, itemsById)).ToList();
            ai.RestoreExecutionState(
                currentJob,
                pendingJobs,
                crewDto.CurrentPath.Select(c => new GridCoord(c.X, c.Y)),
                crewDto.MovementAccumulator,
                crewDto.CurrentActivity);
        }

        // Restore Scenario
        if (!string.IsNullOrEmpty(dto.ScenarioId))
        {
            Scenario? scenario = ScenarioCatalog.CreateById(dto.ScenarioId);

            if (scenario != null)
            {
                scenario.ElapsedSeconds = dto.ScenarioElapsed ?? 0.0;
                scenario.TimeLimitSeconds = dto.ScenarioTimeLimit ?? scenario.TimeLimitSeconds;
                scenario.IsCompleted = dto.ScenarioCompleted;
                scenario.IsFailed = dto.ScenarioFailed;
                scenario.FailureReason = dto.ScenarioFailureReason;
                foreach (var objectiveDto in dto.ScenarioObjectives)
                {
                    var objective = scenario.Objectives.FirstOrDefault(o => o.Id == objectiveDto.Id);
                    if (objective != null)
                    {
                        objective.IsCompleted = objectiveDto.IsCompleted;
                        objective.IsFailed = objectiveDto.IsFailed;
                    }
                }
                sim.ActiveScenario = scenario;
            }
        }

        return sim;
    }

    private static ItemSaveDto ToItemDto(Item item) => new()
    {
        Id = item.Id,
        Type = item.Type,
        Name = item.Name,
        X = item.Position.X,
        Y = item.Position.Y,
        WeightKg = item.WeightKg,
        O2Moles = item.StoredOxygenMoles,
        Nutrition = item.NutritionValue,
        Hydration = item.HydrationValue,
        IsCarried = item.IsCarried,
        ReservedByCrewId = item.ReservedByCrewId
    };

    private static Item FromItemDto(ItemSaveDto dto)
    {
        var item = new Item(dto.Type, new GridCoord(dto.X, dto.Y), dto.WeightKg, dto.Name)
        {
            StoredOxygenMoles = dto.O2Moles,
            NutritionValue = dto.Nutrition,
            HydrationValue = dto.Hydration,
            IsCarried = dto.IsCarried,
            ReservedByCrewId = dto.ReservedByCrewId
        };
        item.RestoreId(dto.Id);
        return item;
    }

    private static CoordDto ToCoordDto(GridCoord coord) => new() { X = coord.X, Y = coord.Y };

    private static JobSaveDto ToJobDto(Job job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Type = job.Type,
        TargetX = job.TargetCoord.X,
        TargetY = job.TargetCoord.Y,
        TargetItemId = job.TargetItem?.Id,
        DesiredItemType = job.DesiredItemType,
        Priority = job.Priority,
        AssignedCrewId = job.AssignedCrewId,
        WorkRemaining = job.WorkRemainingSeconds,
        IsCompleted = job.IsCompleted,
        IsCancelled = job.IsCancelled
    };

    private static Job ResolveJob(JobSaveDto dto, Dictionary<Guid, Job> jobsById, Dictionary<Guid, Item> itemsById)
    {
        if (jobsById.TryGetValue(dto.Id, out var existing)) return existing;
        var job = FromJobDto(dto, itemsById);
        jobsById[job.Id] = job;
        return job;
    }

    private static Job FromJobDto(JobSaveDto dto, Dictionary<Guid, Item> itemsById)
    {
        var job = new Job(dto.Type, new GridCoord(dto.TargetX, dto.TargetY), dto.Priority, dto.WorkRemaining, dto.Name)
        {
            DesiredItemType = dto.DesiredItemType,
            AssignedCrewId = dto.AssignedCrewId,
            IsCompleted = dto.IsCompleted,
            IsCancelled = dto.IsCancelled
        };
        if (dto.TargetItemId.HasValue && itemsById.TryGetValue(dto.TargetItemId.Value, out var item))
            job.TargetItem = item;
        if (dto.Id != Guid.Empty)
            job.RestoreId(dto.Id);
        return job;
    }
}

public sealed class SimulationSaveDto
{
    public double SimTime { get; set; }
    public double UpdateAccumulator { get; set; }
    public double CableCapacityKW { get; set; } = 50.0;
    public double CoolantCapacityLiters { get; set; } = 100.0;
    public double CoolantStoredLiters { get; set; } = 100.0;
    public double CoolantReserveLiters { get; set; } = 100.0;
    public double CoolantLeakRateLitersPerSecond { get; set; } = 0.25;
    public double PropellantPipeCapacityKgPerSecond { get; set; } = 5.0;
    public bool IsPaused { get; set; }
    public double TimeScale { get; set; }
    public int GridWidth { get; set; }
    public int GridHeight { get; set; }
    public List<TileSaveDto> Tiles { get; set; } = new();
    public List<MachineSaveDto> Machines { get; set; } = new();
    public List<CoordDto> Cables { get; set; } = new();
    public List<CoordDto> CoolantPipes { get; set; } = new();
    public List<CoordDto> PropellantPipes { get; set; } = new();
    public List<ItemSaveDto> WorldItems { get; set; } = new();
    public List<JobSaveDto> Jobs { get; set; } = new();
    public List<CrewSaveDto> Crew { get; set; } = new();
    public string? ScenarioId { get; set; }
    public double? ScenarioTimeLimit { get; set; }
    public double? ScenarioElapsed { get; set; }
    public bool ScenarioCompleted { get; set; }
    public bool ScenarioFailed { get; set; }
    public string? ScenarioFailureReason { get; set; }
    public List<ScenarioObjectiveSaveDto> ScenarioObjectives { get; set; } = new();
}

public sealed class TileSaveDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public TileType Type { get; set; }
    public DoorState DoorState { get; set; }
    public bool IsBreached { get; set; }
    public double O2 { get; set; }
    public double N2 { get; set; }
    public double CO2 { get; set; }
    public double ToxicGas { get; set; }
    public double TempK { get; set; }
    public double Volume { get; set; } = GasMix.DefaultTileVolume;
    public int IronOre { get; set; }
    public int IceOre { get; set; }
}

public sealed class MachineSaveDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public MachineType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsOnline { get; set; }
    public bool IsPowered { get; set; }
    public double PowerOutput { get; set; }
    public double RatedPowerOutput { get; set; }
    public double PowerDemand { get; set; }
    public double BatteryMaxCharge { get; set; } = 50.0;
    public double BatteryMaxDischarge { get; set; } = 50.0;
    public double O2GenerationRate { get; set; } = 0.5;
    public double CO2ScrubbingRate { get; set; } = 0.25;
    public double CoreTempK { get; set; }
    public double HeatGeneration { get; set; } = 12.0;
    public double HeatCapacity { get; set; } = 100_000.0;
    public double MaxSafeTempK { get; set; } = 1000.0;
    public double ScramTempK { get; set; } = 1200.0;
    public double ScramResetTempK { get; set; } = 600.0;
    public bool IsScrammed { get; set; }
    public double RadiatorCooling { get; set; } = 15.0;
    public double CoolantPumpFlowRate { get; set; } = 0.5;
    public double BatteryStored { get; set; }
    public double BatteryCapacity { get; set; }
    public int? TargetAsteroidX { get; set; }
    public int? TargetAsteroidY { get; set; }
    public double MiningProgress { get; set; }
    public double MiningCycle { get; set; } = 3.0;
    public int IronOreStored { get; set; }
    public int SteelPlatesProduced { get; set; }
    public double SmeltProgress { get; set; }
    public double SmeltCycle { get; set; } = 4.0;
    public double WaterStored { get; set; }
    public int FoodProduced { get; set; }
    public double GrowthProgress { get; set; }
    public double GrowthCycle { get; set; } = 10.0;
    public double FuelStored { get; set; }
    public double FuelCapacity { get; set; }
    public bool IsFiring { get; set; }
    public double Thrust { get; set; } = 50.0;
    public double FuelBurnRate { get; set; } = 2.5;
    public int TotalOreExtracted { get; set; }
}

public sealed class CoordDto
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class ItemSaveDto
{
    public Guid Id { get; set; }
    public ItemType Type { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public double WeightKg { get; set; }
    public double O2Moles { get; set; }
    public double Nutrition { get; set; }
    public double Hydration { get; set; }
    public bool IsCarried { get; set; }
    public Guid? ReservedByCrewId { get; set; }
}

public sealed class CrewSaveDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public double MoveSpeed { get; set; }
    public bool IsEquippedWithSuit { get; set; }
    public string CurrentActivity { get; set; } = "Idle";
    public JobSaveDto? CurrentJob { get; set; }
    public List<JobSaveDto> PendingSubJobs { get; set; } = new();
    public List<CoordDto> CurrentPath { get; set; } = new();
    public double MovementAccumulator { get; set; }
    public int? BunkX { get; set; }
    public int? BunkY { get; set; }
    public double Health { get; set; }
    public double O2Sat { get; set; }
    public double Hunger { get; set; }
    public double Thirst { get; set; }
    public double Fatigue { get; set; }
    public double Courage { get; set; } = 50.0;
    public double? Discipline { get; set; }
    public double SelfPreservation { get; set; } = 50.0;
    public double? Determination { get; set; }
    public double? Sociability { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? WorkEthic { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temper { get; set; }
    public double Fear { get; set; }
    public double? Stress { get; set; }
    public double Anger { get; set; }
    public double Motivation { get; set; } = 50.0;
    public double? Morale { get; set; }
    public List<ItemSaveDto> Inventory { get; set; } = new();
    public ItemSaveDto? EquippedTank { get; set; }
}

public sealed class JobSaveDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public JobType Type { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public Guid? TargetItemId { get; set; }
    public ItemType? DesiredItemType { get; set; }
    public int Priority { get; set; }
    public Guid? AssignedCrewId { get; set; }
    public double WorkRemaining { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }
}

public sealed class ScenarioObjectiveSaveDto
{
    public string Id { get; set; } = "";
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
}
