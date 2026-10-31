using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// <summary>
    /// Current save format. Version 3 added job lifecycle state, version 4 added job
    /// material/tool reservations, and version 5 adds machine production controls.
    /// Versions 0-4 remain readable with older features defaulted.
    /// </summary>
    public const int CurrentSaveFormatVersion = 5;
    public const int MinimumSupportedSaveFormatVersion = 0;
    private const long MaximumSavedGridTileCount = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void SaveToFile(SimulationState sim, string path)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] saveBytes = Encoding.UTF8.GetBytes(SaveToJson(sim));
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(saveBytes);
                stream.Flush(flushToDisk: true);
            }

            // The temporary file is on the same volume, so the destination is only
            // replaced after the complete save has been written and flushed.
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Preserve the original write/replace error if best-effort cleanup fails.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the original write/replace error if best-effort cleanup fails.
            }
        }
    }

    public static SimulationState LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadFromJson(File.ReadAllText(path));
    }

    public static string SaveToJson(SimulationState sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        var dto = new SimulationSaveDto
        {
            SaveFormatVersion = CurrentSaveFormatVersion,
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
            AtmosphereDiffusionRate = sim.AtmosphereSolver.DiffusionRate,
            AtmosphereVentingRate = sim.AtmosphereSolver.VentingRate,
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
                PowerFraction = m.PowerFraction,
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
                TotalOreExtracted = m.TotalOreExtracted,
                ComponentsProduced = m.ComponentsProduced,
                ComponentFabricationProgress = m.ComponentFabricationProgressSeconds,
                ComponentFabricationCycle = m.ComponentFabricationCycleSeconds,
                IsComponentFabricationInProgress = m.IsComponentFabricationInProgress,
                WaterRationsProduced = m.WaterRationsProduced,
                FuelCanistersProduced = m.FuelCanistersProduced,
                ProduceFuelCanisters = m.ProduceFuelCanisters,
                MiningRange = m.MiningRange,
                IceProcessingProgress = m.IceProcessingProgressSeconds,
                IceProcessingCycle = m.IceProcessingCycleSeconds,
                IsIceProcessingInProgress = m.IsIceProcessingInProgress,
                ProductionPaused = m.ProductionPaused,
                ProductionLimit = m.ProductionLimit,
                ProductionCyclesCompleted = m.ProductionCyclesCompleted
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
                DeferredJobId = c.DeferredJobId,
                DeferredJobTimer = c.DeferredJobTimer,
                IsRequestingAssistance = c.IsRequestingAssistance,
                ConsecutiveTaskFailures = c.ConsecutiveTaskFailures,
                IsRetreating = c.IsRetreating,
                RetreatTargetX = c.RetreatTarget?.X,
                RetreatTargetY = c.RetreatTarget?.Y,
                RetreatRetryTimer = c.RetreatRetryTimer,
                LastDiagnostic = c.LastDiagnostic != null ? ToDiagnosticDto(c.LastDiagnostic) : null,
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
                Inventory = c.Crew.Inventory.Select(item =>
                {
                    item.IsCarried = true;
                    return ToItemDto(item);
                }).ToList(),
                EquippedTank = c.Crew.EquippedOxygenTank != null ? ToCarriedItemDto(c.Crew.EquippedOxygenTank) : null
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

        ValidateSaveData(dto);
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static SimulationState LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        SimulationSaveDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<SimulationSaveDto>(json, JsonOptions)
                  ?? throw new InvalidDataException("Simulation save data is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Simulation save data is not valid JSON.", exception);
        }

        ValidateSaveData(dto);

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
        jobManager.AdvanceClock(dto.SimTime);
        var atmosphereSolver = new AtmosphereSolver();
        var sim = new SimulationState(grid, atmosphereSolver, jobManager);

        sim.IsPaused = dto.IsPaused;
        sim.TimeScale = dto.TimeScale;
        sim.RestoreClock(dto.SimTime, dto.UpdateAccumulator);
        sim.AtmosphereSolver.DiffusionRate = dto.AtmosphereDiffusionRate;
        sim.AtmosphereSolver.VentingRate = dto.AtmosphereVentingRate;
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
                TotalOreExtracted = mDto.TotalOreExtracted,
                ComponentsProduced = mDto.ComponentsProduced,
                ComponentFabricationProgressSeconds = mDto.ComponentFabricationProgress,
                ComponentFabricationCycleSeconds = mDto.ComponentFabricationCycle,
                IsComponentFabricationInProgress = mDto.IsComponentFabricationInProgress,
                WaterRationsProduced = mDto.WaterRationsProduced,
                FuelCanistersProduced = mDto.FuelCanistersProduced,
                ProduceFuelCanisters = mDto.ProduceFuelCanisters,
                MiningRange = mDto.MiningRange > 0 ? mDto.MiningRange : 6,
                IceProcessingProgressSeconds = mDto.IceProcessingProgress,
                IceProcessingCycleSeconds = mDto.IceProcessingCycle,
                IsIceProcessingInProgress = mDto.IsIceProcessingInProgress,
                ProductionPaused = mDto.ProductionPaused,
                ProductionLimit = mDto.ProductionLimit,
                ProductionCyclesCompleted = mDto.ProductionCyclesCompleted
            };
            m.SetPowerFraction(mDto.PowerFraction ?? (mDto.IsPowered ? 1.0 : 0.0));
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
            var job = FromJobDto(jobDto, itemsById, dto.SaveFormatVersion);
            jobsById[job.Id] = job;
            jobManager.PostJob(job);
        }

        var crewAisById = sim.Crew.ToDictionary(c => c.Crew.Id);
        foreach (var crewDto in dto.Crew)
        {
            var ai = crewAisById[crewDto.Id];
            Job? currentJob = crewDto.CurrentJob != null
                ? ResolveJob(crewDto.CurrentJob, jobsById, itemsById, dto.SaveFormatVersion)
                : null;
            var pendingJobs = crewDto.PendingSubJobs
                .Select(j => ResolveJob(j, jobsById, itemsById, dto.SaveFormatVersion))
                .ToList();
            ai.RestoreExecutionState(
                currentJob,
                pendingJobs,
                crewDto.CurrentPath.Select(c => new GridCoord(c.X, c.Y)),
                crewDto.MovementAccumulator,
                crewDto.CurrentActivity,
                crewDto.DeferredJobId,
                crewDto.DeferredJobTimer,
                crewDto.IsRequestingAssistance,
                crewDto.ConsecutiveTaskFailures,
                crewDto.IsRetreating,
                crewDto.RetreatTargetX.HasValue
                    ? new GridCoord(crewDto.RetreatTargetX.Value, crewDto.RetreatTargetY!.Value)
                    : null,
                crewDto.RetreatRetryTimer,
                crewDto.LastDiagnostic != null ? FromDiagnosticDto(crewDto.LastDiagnostic) : null);
        }

        // Restore Scenario
        if (!string.IsNullOrEmpty(dto.ScenarioId))
        {
            Scenario? scenario = ScenarioCatalog.CreateById(dto.ScenarioId);

            if (scenario == null)
                throw new InvalidDataException($"Save data references unknown scenario '{dto.ScenarioId}'.");

            scenario.ElapsedSeconds = dto.ScenarioElapsed ?? 0.0;
            scenario.TimeLimitSeconds = dto.ScenarioTimeLimit ?? scenario.TimeLimitSeconds;
            scenario.IsCompleted = dto.ScenarioCompleted;
            scenario.IsFailed = dto.ScenarioFailed;
            scenario.FailureReason = dto.ScenarioFailureReason;
            foreach (var objectiveDto in dto.ScenarioObjectives)
            {
                var objective = scenario.Objectives.FirstOrDefault(o => o.Id == objectiveDto.Id)
                    ?? throw new InvalidDataException(
                        $"Save data references unknown objective '{objectiveDto.Id}' for scenario '{dto.ScenarioId}'.");
                objective.IsCompleted = objectiveDto.IsCompleted;
                objective.IsFailed = objectiveDto.IsFailed;
            }

            sim.ActiveScenario = scenario;
        }

        return sim;
    }

    private static void ValidateSaveData(SimulationSaveDto dto)
    {
        if (dto.SaveFormatVersion < 0 || dto.SaveFormatVersion > CurrentSaveFormatVersion)
            throw new NotSupportedException(
                $"Save format version {dto.SaveFormatVersion} is not supported. This build supports versions 0 through {CurrentSaveFormatVersion}.");

        if (dto.GridWidth <= 0 || dto.GridHeight <= 0)
            throw new InvalidDataException("Save data must contain positive grid dimensions.");

        long tileCount = (long)dto.GridWidth * dto.GridHeight;
        if (tileCount > MaximumSavedGridTileCount)
            throw new InvalidDataException(
                $"Saved grid contains {tileCount} tiles; the maximum supported by this build is {MaximumSavedGridTileCount}.");

        if (dto.Tiles == null || dto.Machines == null || dto.Cables == null ||
            dto.CoolantPipes == null || dto.PropellantPipes == null || dto.WorldItems == null ||
            dto.Jobs == null || dto.Crew == null || dto.ScenarioObjectives == null)
            throw new InvalidDataException("Save data contains a missing or null collection.");

        RequireFiniteNonNegative(dto.SimTime, "simulation time");
        RequireFiniteNonNegative(dto.UpdateAccumulator, "update accumulator");
        if (!double.IsFinite(dto.TimeScale) || dto.TimeScale <= 0.0)
            throw new InvalidDataException("Save data contains an invalid time scale.");
        double[] plumbingValues =
        [
            dto.CableCapacityKW,
            dto.CoolantCapacityLiters,
            dto.CoolantStoredLiters,
            dto.CoolantReserveLiters,
            dto.CoolantLeakRateLitersPerSecond,
            dto.PropellantPipeCapacityKgPerSecond
        ];
        if (plumbingValues.Any(value => !double.IsFinite(value) || value < 0.0))
            throw new InvalidDataException("Save data contains invalid utility system values.");
        if (!double.IsFinite(dto.AtmosphereDiffusionRate) || dto.AtmosphereDiffusionRate < 0.0 ||
            !double.IsFinite(dto.AtmosphereVentingRate) || dto.AtmosphereVentingRate < 0.0)
            throw new InvalidDataException("Save data contains invalid atmosphere solver values.");

        if (dto.Tiles.Count != tileCount)
            throw new InvalidDataException(
                $"Save data contains {dto.Tiles.Count} tiles, but the {dto.GridWidth}x{dto.GridHeight} grid requires {tileCount}.");

        var tileCoordinates = new HashSet<(int X, int Y)>();
        foreach (var tile in dto.Tiles)
        {
            if (tile == null)
                throw new InvalidDataException("Save data contains a null tile.");
            ValidateCoordinate(tile.X, tile.Y, dto.GridWidth, dto.GridHeight, "tile");
            if (!tileCoordinates.Add((tile.X, tile.Y)))
                throw new InvalidDataException($"Save data contains duplicate tile ({tile.X}, {tile.Y}).");
            if (!Enum.IsDefined(typeof(TileType), tile.Type) || !Enum.IsDefined(typeof(DoorState), tile.DoorState))
                throw new InvalidDataException($"Tile ({tile.X}, {tile.Y}) contains an unknown tile or door type.");
            RequireFiniteNonNegative(tile.O2, "tile oxygen");
            RequireFiniteNonNegative(tile.N2, "tile nitrogen");
            RequireFiniteNonNegative(tile.CO2, "tile carbon dioxide");
            RequireFiniteNonNegative(tile.ToxicGas, "tile toxic gas");
            if (tile.IronOre < 0 || tile.IceOre < 0)
                throw new InvalidDataException($"Tile ({tile.X}, {tile.Y}) contains a negative resource quantity.");
            if (!double.IsFinite(tile.TempK) || tile.TempK <= 0.0 ||
                !double.IsFinite(tile.Volume) || tile.Volume <= 0.0)
                throw new InvalidDataException($"Tile ({tile.X}, {tile.Y}) contains invalid atmosphere values.");
        }

        ValidateCoordinates(dto.Cables, dto.GridWidth, dto.GridHeight, "cable");
        ValidateCoordinates(dto.CoolantPipes, dto.GridWidth, dto.GridHeight, "coolant pipe");
        ValidateCoordinates(dto.PropellantPipes, dto.GridWidth, dto.GridHeight, "propellant pipe");

        var machineIds = new HashSet<Guid>();
        foreach (var machine in dto.Machines)
        {
            if (machine == null)
                throw new InvalidDataException("Save data contains a null machine.");
            ValidateCoordinate(machine.X, machine.Y, dto.GridWidth, dto.GridHeight, "machine");
            ValidateId(machine.Id, machineIds, "machine");
            if (machine.Name == null)
                throw new InvalidDataException("Save data contains a machine with a null name.");
            if (!Enum.IsDefined(typeof(MachineType), machine.Type))
                throw new InvalidDataException($"Machine '{machine.Name}' contains an unknown type.");
            double[] machineValues =
            [
                machine.PowerOutput, machine.RatedPowerOutput, machine.PowerDemand,
                machine.BatteryMaxCharge, machine.BatteryMaxDischarge,
                machine.O2GenerationRate, machine.CO2ScrubbingRate, machine.CoreTempK,
                machine.HeatGeneration, machine.HeatCapacity, machine.MaxSafeTempK,
                machine.ScramTempK, machine.ScramResetTempK, machine.RadiatorCooling,
                machine.CoolantPumpFlowRate, machine.BatteryStored, machine.BatteryCapacity,
                machine.MiningProgress, machine.MiningCycle, machine.SmeltProgress,
                machine.SmeltCycle, machine.WaterStored, machine.GrowthProgress,
                machine.GrowthCycle, machine.FuelStored, machine.FuelCapacity,
                machine.Thrust, machine.FuelBurnRate, machine.ComponentFabricationProgress,
                machine.ComponentFabricationCycle, machine.IceProcessingProgress,
                machine.IceProcessingCycle
            ];
            if (machineValues.Any(value => !double.IsFinite(value) || value < 0.0) ||
                machine.IronOreStored < 0 || machine.SteelPlatesProduced < 0 ||
                machine.FoodProduced < 0 || machine.TotalOreExtracted < 0 ||
                machine.ComponentsProduced < 0 || machine.WaterRationsProduced < 0 ||
                machine.ComponentFabricationCycle <= 0.0 || machine.IceProcessingCycle <= 0.0)
                throw new InvalidDataException($"Machine '{machine.Name}' contains invalid operating values.");
            if (machine.PowerFraction.HasValue &&
                (!double.IsFinite(machine.PowerFraction.Value) || machine.PowerFraction.Value < 0.0 || machine.PowerFraction.Value > 1.0))
                throw new InvalidDataException($"Machine '{machine.Name}' contains an invalid power fraction.");
            if (machine.TargetAsteroidX.HasValue != machine.TargetAsteroidY.HasValue)
                throw new InvalidDataException($"Machine '{machine.Name}' contains an incomplete asteroid coordinate.");
            if (machine.ProductionLimit < -1 || machine.ProductionCyclesCompleted < 0)
                throw new InvalidDataException($"Machine '{machine.Name}' contains invalid production controls.");
            if (machine.TargetAsteroidX.HasValue)
                ValidateCoordinate(machine.TargetAsteroidX.Value, machine.TargetAsteroidY!.Value,
                    dto.GridWidth, dto.GridHeight, "asteroid target");
        }

        var crewIds = new HashSet<Guid>();
        var itemIds = new HashSet<Guid>();
        foreach (var item in dto.WorldItems)
            ValidateItem(item, itemIds, dto.GridWidth, dto.GridHeight);

        foreach (var crew in dto.Crew)
        {
            if (crew == null)
                throw new InvalidDataException("Save data contains a null crew member.");
            ValidateCoordinate(crew.X, crew.Y, dto.GridWidth, dto.GridHeight, "crew member");
            ValidateId(crew.Id, crewIds, "crew member");
            if (crew.Name == null || crew.Role == null || crew.CurrentActivity == null)
                throw new InvalidDataException("Save data contains a crew member with missing identity or activity data.");
            double[] crewRates = [crew.MoveSpeed, crew.MovementAccumulator, crew.DeferredJobTimer];
            if (crewRates.Any(value => !double.IsFinite(value) || value < 0.0))
                throw new InvalidDataException($"Crew member '{crew.Name}' contains invalid movement values.");
            double[] crewStates =
            [
                crew.Health, crew.O2Sat, crew.Hunger, crew.Thirst, crew.Fatigue,
                crew.Courage, crew.Discipline ?? crew.WorkEthic ?? 50.0,
                crew.SelfPreservation, crew.Determination ?? 50.0,
                crew.Sociability ?? 50.0, crew.Fear, crew.Stress ?? 0.0,
                crew.Anger, crew.Motivation, crew.Morale ?? 70.0,
                crew.Temper ?? 0.0
            ];
            if (crewStates.Any(value => !double.IsFinite(value) || value < 0.0 || value > 100.0))
                throw new InvalidDataException($"Crew member '{crew.Name}' contains invalid physiology or psychology values.");
            if (crew.BunkX.HasValue != crew.BunkY.HasValue)
                throw new InvalidDataException($"Crew member '{crew.Name}' contains an incomplete bunk coordinate.");
            if (crew.BunkX.HasValue)
                ValidateCoordinate(crew.BunkX.Value, crew.BunkY!.Value, dto.GridWidth, dto.GridHeight, "bunk");
            if (crew.RetreatTargetX.HasValue != crew.RetreatTargetY.HasValue)
                throw new InvalidDataException($"Crew member '{crew.Name}' contains an incomplete retreat coordinate.");
            if (crew.RetreatTargetX.HasValue)
                ValidateCoordinate(crew.RetreatTargetX.Value, crew.RetreatTargetY!.Value,
                    dto.GridWidth, dto.GridHeight, "retreat target");
            if (crew.IsRetreating != crew.RetreatTargetX.HasValue)
                throw new InvalidDataException($"Crew member '{crew.Name}' contains inconsistent retreat state.");
            RequireFiniteNonNegative(crew.RetreatRetryTimer, "retreat retry timer");
            if (crew.ConsecutiveTaskFailures < 0)
                throw new InvalidDataException($"Crew member '{crew.Name}' contains a negative task failure count.");
            if (crew.LastDiagnostic != null)
                ValidateDiagnostic(crew.LastDiagnostic, dto.GridWidth, dto.GridHeight);
            if (crew.Inventory == null || crew.CurrentPath == null || crew.PendingSubJobs == null)
                throw new InvalidDataException($"Crew member '{crew.Name}' contains a missing or null collection.");
            foreach (var item in crew.Inventory)
            {
                ValidateItem(item, itemIds, dto.GridWidth, dto.GridHeight);
                if (!item.IsCarried)
                    throw new InvalidDataException($"Crew member '{crew.Name}' inventory contains an item that is not marked as carried.");
            }
            if (crew.EquippedTank != null)
            {
                ValidateItem(crew.EquippedTank, itemIds, dto.GridWidth, dto.GridHeight);
                if (!crew.EquippedTank.IsCarried)
                    throw new InvalidDataException($"Crew member '{crew.Name}' equipped tank is not marked as carried.");
            }
            foreach (var coord in crew.CurrentPath)
            {
                if (coord == null)
                    throw new InvalidDataException($"Crew member '{crew.Name}' contains a null path coordinate.");
                ValidateCoordinate(coord.X, coord.Y, dto.GridWidth, dto.GridHeight, "crew path");
            }
        }

        var jobIds = new HashSet<Guid>();
        foreach (var job in dto.Jobs)
        {
            ValidateJob(job, dto.GridWidth, dto.GridHeight);
            ValidateId(job.Id, jobIds, "job");
        }
        foreach (var crew in dto.Crew)
        {
            if (crew.CurrentJob != null)
                ValidateJob(crew.CurrentJob, dto.GridWidth, dto.GridHeight);
            foreach (var job in crew.PendingSubJobs)
                ValidateJob(job, dto.GridWidth, dto.GridHeight);
        }

        foreach (var job in dto.Jobs.Concat(dto.Crew
                     .SelectMany(crew => (crew.CurrentJob == null
                         ? Enumerable.Empty<JobSaveDto>()
                         : new[] { crew.CurrentJob }).Concat(crew.PendingSubJobs))))
        {
            if (job.TargetItemId.HasValue && !itemIds.Contains(job.TargetItemId.Value))
                throw new InvalidDataException($"Job '{job.Name}' references a missing item.");
            if (job.ReservedMaterialIds != null && job.ReservedMaterialIds.Any(id => !itemIds.Contains(id)))
                throw new InvalidDataException($"Job '{job.Name}' references a missing reserved item.");
            if (job.AssignedCrewId.HasValue && !crewIds.Contains(job.AssignedCrewId.Value))
                throw new InvalidDataException($"Job '{job.Name}' references a missing assigned crew member.");
            if (job.TargetCrewId.HasValue && !crewIds.Contains(job.TargetCrewId.Value))
                throw new InvalidDataException($"Job '{job.Name}' references a missing target crew member.");
        }

        foreach (var item in dto.WorldItems.Concat(dto.Crew
                     .SelectMany(crew => crew.Inventory.Concat(
                         crew.EquippedTank == null
                             ? Enumerable.Empty<ItemSaveDto>()
                             : new[] { crew.EquippedTank }))))
        {
            if (item.IsCarried && !dto.Crew.Any(crew =>
                    crew.Inventory.Any(inventoryItem => inventoryItem.Id == item.Id) ||
                    crew.EquippedTank?.Id == item.Id))
                throw new InvalidDataException($"Item '{item.Name}' is marked as carried but is not owned by a crew member.");
            if (item.ReservedByCrewId.HasValue && !crewIds.Contains(item.ReservedByCrewId.Value))
                throw new InvalidDataException($"Item '{item.Name}' is reserved by a missing crew member.");
        }

        if (dto.ScenarioCompleted && dto.ScenarioFailed)
            throw new InvalidDataException("Save data cannot mark a scenario both completed and failed.");
        if (dto.ScenarioElapsed.HasValue)
            RequireFiniteNonNegative(dto.ScenarioElapsed.Value, "scenario elapsed time");
        if (dto.ScenarioTimeLimit.HasValue &&
            (!double.IsFinite(dto.ScenarioTimeLimit.Value) || dto.ScenarioTimeLimit.Value <= 0.0))
            throw new InvalidDataException("Save data contains an invalid scenario time limit.");
        if (string.IsNullOrWhiteSpace(dto.ScenarioId) &&
            (dto.ScenarioCompleted || dto.ScenarioFailed || dto.ScenarioObjectives.Count > 0 ||
             dto.ScenarioElapsed.HasValue || dto.ScenarioTimeLimit.HasValue || dto.ScenarioFailureReason != null))
            throw new InvalidDataException("Save data contains scenario progress without a scenario id.");
        if (dto.ScenarioObjectives.Any(objective => objective == null || string.IsNullOrWhiteSpace(objective.Id)))
            throw new InvalidDataException("Save data contains an invalid scenario objective.");
        if (dto.ScenarioObjectives.Any(objective => objective.IsCompleted && objective.IsFailed))
            throw new InvalidDataException("A scenario objective cannot be both completed and failed.");
        if (dto.ScenarioObjectives.Select(objective => objective.Id).Distinct(StringComparer.Ordinal).Count() !=
            dto.ScenarioObjectives.Count)
            throw new InvalidDataException("Save data contains duplicate scenario objective ids.");
        if (dto.ScenarioId != null && string.IsNullOrWhiteSpace(dto.ScenarioId))
            throw new InvalidDataException("Save data contains an empty scenario id.");
        if (dto.ScenarioId != null)
        {
            Scenario scenario = ScenarioCatalog.CreateById(dto.ScenarioId)
                ?? throw new InvalidDataException($"Save data references unknown scenario '{dto.ScenarioId}'.");
            if (dto.ScenarioObjectives.Any(savedObjective =>
                    scenario.Objectives.All(objective => objective.Id != savedObjective.Id)))
                throw new InvalidDataException($"Save data contains an objective not defined by scenario '{dto.ScenarioId}'.");
        }
    }

    private static void ValidateCoordinates(
        IEnumerable<CoordDto> coordinates, int width, int height, string description)
    {
        foreach (var coord in coordinates)
        {
            if (coord == null)
                throw new InvalidDataException($"Save data contains a null {description} coordinate.");
            ValidateCoordinate(coord.X, coord.Y, width, height, description);
        }
    }

    private static void ValidateCoordinate(int x, int y, int width, int height, string description)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            throw new InvalidDataException($"Save data contains an out-of-bounds {description} coordinate ({x}, {y}).");
    }

    private static void ValidateId(Guid id, HashSet<Guid> seenIds, string description)
    {
        if (id == Guid.Empty)
            throw new InvalidDataException($"Save data contains a {description} with an empty id.");
        if (!seenIds.Add(id))
            throw new InvalidDataException($"Save data contains duplicate {description} id '{id}'.");
    }

    private static void ValidateItem(ItemSaveDto item, HashSet<Guid> seenIds, int width, int height)
    {
        if (item == null)
            throw new InvalidDataException("Save data contains a null item.");
        if (item.Name == null)
            throw new InvalidDataException("Save data contains an item with a null name.");
        ValidateCoordinate(item.X, item.Y, width, height, "item");
        ValidateId(item.Id, seenIds, "item");
        if (!Enum.IsDefined(typeof(ItemType), item.Type))
            throw new InvalidDataException($"Item '{item.Name}' contains an unknown type.");
        RequireFiniteNonNegative(item.WeightKg, "item weight");
        RequireFiniteNonNegative(item.O2Moles, "item oxygen");
        RequireFiniteNonNegative(item.Nutrition, "item nutrition");
        RequireFiniteNonNegative(item.Hydration, "item hydration");
    }

    private static void ValidateJob(JobSaveDto job, int width, int height)
    {
        if (job == null)
            throw new InvalidDataException("Save data contains a null job.");
        ValidateCoordinate(job.TargetX, job.TargetY, width, height, "job target");
        if (job.Id == Guid.Empty)
            throw new InvalidDataException("Save data contains a job with an empty id.");
        if (!Enum.IsDefined(typeof(JobType), job.Type))
            throw new InvalidDataException($"Job '{job.Name}' contains an unknown type.");
        if (!Enum.IsDefined(typeof(JobState), job.State))
            throw new InvalidDataException($"Job '{job.Name}' contains an unknown lifecycle state.");
        if (job.FailedAttempts < 0)
            throw new InvalidDataException($"Job '{job.Name}' contains a negative failed-attempt count.");
        RequireFiniteNonNegative(job.RetryNotBeforeSimTime, "job retry time");
        if (job.State.IsTerminal() && job.AssignedCrewId.HasValue)
            throw new InvalidDataException($"Job '{job.Name}' is terminal but still has an assignee.");
        if (job.State == JobState.Reserved && !job.AssignedCrewId.HasValue)
            throw new InvalidDataException($"Job '{job.Name}' is reserved but has no assignee.");
        if (job.State == JobState.Completed && job.IsCancelled ||
            job.State == JobState.Cancelled && job.IsCompleted)
            throw new InvalidDataException($"Job '{job.Name}' has contradictory completion flags.");
        if (job.State == JobState.Completed && !job.IsCompleted ||
            job.State == JobState.Cancelled && !job.IsCancelled)
            throw new InvalidDataException($"Job '{job.Name}' has flags inconsistent with its lifecycle state.");
        if (job.BlockedReason is { Length: > 512 } || job.LastFailureReason is { Length: > 512 })
            throw new InvalidDataException($"Job '{job.Name}' contains an overlong failure reason.");
        if (job.Name == null ||
            (job.DesiredItemType.HasValue && !Enum.IsDefined(typeof(ItemType), job.DesiredItemType.Value)) ||
            (job.AssistanceReason.HasValue && !Enum.IsDefined(typeof(CrewDiagnosticReason), job.AssistanceReason.Value)))
            throw new InvalidDataException("Save data contains invalid job metadata.");
        RequireFiniteNonNegative(job.WorkRemaining, "job work remaining");
        if (job.AssistanceItemTypes == null || job.FailedAssistantCrewIds == null)
            throw new InvalidDataException($"Job '{job.Name}' contains a missing or null collection.");
        if (job.AssistanceItemTypes.Any(type => !Enum.IsDefined(typeof(ItemType), type)))
            throw new InvalidDataException($"Job '{job.Name}' contains an unknown assistance item type.");
        if (job.RequiredSteelPlates < 0 || job.RequiredMachineComponents < 0)
            throw new InvalidDataException($"Job '{job.Name}' contains negative construction costs.");
        if (job.ReservedMaterialIds == null)
            throw new InvalidDataException($"Job '{job.Name}' contains a missing material reservation list.");
        if (job.ReservedMaterialIds.Any(id => id == Guid.Empty) ||
            job.ReservedMaterialIds.Distinct().Count() != job.ReservedMaterialIds.Count)
            throw new InvalidDataException($"Job '{job.Name}' contains invalid material reservations.");
        if (job.Type == JobType.Construct)
        {
            if (job.ConstructTileType.HasValue == job.ConstructMachineType.HasValue ||
                job.ConstructTileType.HasValue && !Enum.IsDefined(typeof(TileType), job.ConstructTileType.Value) ||
                job.ConstructMachineType.HasValue && !Enum.IsDefined(typeof(MachineType), job.ConstructMachineType.Value))
                throw new InvalidDataException($"Job '{job.Name}' contains an invalid construction target.");
        }
        else if (job.Type == JobType.Deconstruct)
        {
            if (job.ConstructMachineType.HasValue && !Enum.IsDefined(typeof(MachineType), job.ConstructMachineType.Value))
                throw new InvalidDataException($"Job '{job.Name}' contains an invalid deconstruction target.");
        }
        else if (job.ConstructTileType.HasValue || job.ConstructMachineType.HasValue ||
                 job.RequiredSteelPlates > 0 || job.RequiredMachineComponents > 0)
        {
            throw new InvalidDataException($"Non-construction job '{job.Name}' contains construction data.");
        }
    }

    private static void ValidateDiagnostic(
        CrewDiagnosticSaveDto diagnostic, int width, int height)
    {
        if (!Enum.IsDefined(typeof(CrewDiagnosticReason), diagnostic.Reason) ||
            (diagnostic.UnderlyingReason.HasValue &&
             !Enum.IsDefined(typeof(CrewDiagnosticReason), diagnostic.UnderlyingReason.Value)))
            throw new InvalidDataException("Save data contains an unknown crew diagnostic reason.");
        if (diagnostic.Detail == null || diagnostic.FailureCount < 0)
            throw new InvalidDataException("Save data contains invalid crew diagnostic details.");
        ValidateCoordinate(diagnostic.TargetX, diagnostic.TargetY, width, height, "diagnostic target");
        ValidateCoordinate(diagnostic.LastReachableX, diagnostic.LastReachableY, width, height,
            "last reachable tile");
        if (diagnostic.BlockingX.HasValue != diagnostic.BlockingY.HasValue)
            throw new InvalidDataException("Save data contains an incomplete diagnostic blocking coordinate.");
        if (diagnostic.BlockingX.HasValue)
            ValidateCoordinate(diagnostic.BlockingX.Value, diagnostic.BlockingY!.Value,
                width, height, "diagnostic blocking tile");
    }

    private static void RequireFiniteNonNegative(double value, string description)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new InvalidDataException($"Save data contains an invalid {description} value.");
    }

    private static ItemSaveDto ToCarriedItemDto(Item item)
    {
        item.IsCarried = true;
        return ToItemDto(item);
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

    private static CrewDiagnosticSaveDto ToDiagnosticDto(CrewDecisionDiagnostic diagnostic) => new()
    {
        Reason = diagnostic.Reason,
        TargetX = diagnostic.Target.X,
        TargetY = diagnostic.Target.Y,
        LastReachableX = diagnostic.LastReachable.X,
        LastReachableY = diagnostic.LastReachable.Y,
        BlockingX = diagnostic.BlockingTile?.X,
        BlockingY = diagnostic.BlockingTile?.Y,
        Detail = diagnostic.Detail,
        FailureCount = diagnostic.FailureCount,
        UnderlyingReason = diagnostic.UnderlyingReason,
        PhysicalRouteExists = diagnostic.PhysicalRouteExists
    };

    private static CrewDecisionDiagnostic FromDiagnosticDto(CrewDiagnosticSaveDto dto) => new(
        dto.Reason,
        new GridCoord(dto.TargetX, dto.TargetY),
        new GridCoord(dto.LastReachableX, dto.LastReachableY),
        dto.BlockingX.HasValue ? new GridCoord(dto.BlockingX.Value, dto.BlockingY!.Value) : null,
        dto.Detail,
        dto.FailureCount,
        dto.UnderlyingReason,
        dto.PhysicalRouteExists);

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
        TargetCrewId = job.TargetCrewId,
        SourceTaskJobId = job.SourceTaskJobId,
        AssistanceReason = job.AssistanceReason,
        AssistanceItemTypes = job.AssistanceItemTypes.ToList(),
        FailedAssistantCrewIds = job.FailedAssistantCrewIds.ToList(),
        Priority = job.Priority,
        AssignedCrewId = job.AssignedCrewId,
        WorkRemaining = job.WorkRemainingSeconds,
        ConstructTileType = job.ConstructTileType,
        ConstructMachineType = job.ConstructMachineType,
        RequiredSteelPlates = job.RequiredSteelPlates,
        RequiredMachineComponents = job.RequiredMachineComponents,
        IsCompleted = job.IsCompleted,
        IsCancelled = job.IsCancelled,
        State = job.State,
        FailedAttempts = job.FailedAttempts,
        LastFailureReason = job.LastFailureReason,
        BlockedReason = job.BlockedReason,
        RetryNotBeforeSimTime = job.RetryNotBeforeSimTime,
        ReservedMaterialIds = job.ReservedMaterialIds.ToList()
    };

    private static Job ResolveJob(
        JobSaveDto dto,
        Dictionary<Guid, Job> jobsById,
        Dictionary<Guid, Item> itemsById,
        int saveFormatVersion)
    {
        if (jobsById.TryGetValue(dto.Id, out var existing)) return existing;
        var job = FromJobDto(dto, itemsById, saveFormatVersion);
        jobsById[job.Id] = job;
        return job;
    }

    private static Job FromJobDto(JobSaveDto dto, Dictionary<Guid, Item> itemsById, int saveFormatVersion)
    {
        var job = new Job(dto.Type, new GridCoord(dto.TargetX, dto.TargetY), dto.Priority, dto.WorkRemaining, dto.Name)
        {
            DesiredItemType = dto.DesiredItemType,
            AssignedCrewId = dto.AssignedCrewId,
            TargetCrewId = dto.TargetCrewId,
            SourceTaskJobId = dto.SourceTaskJobId,
            AssistanceReason = dto.AssistanceReason,
            ConstructTileType = dto.ConstructTileType,
            ConstructMachineType = dto.ConstructMachineType,
            RequiredSteelPlates = dto.RequiredSteelPlates,
            RequiredMachineComponents = dto.RequiredMachineComponents,
            IsCompleted = dto.IsCompleted,
            IsCancelled = dto.IsCancelled
        };

        // Saves before version 3 only stored terminal flags, so lifecycle state is
        // derived. Version 3+ stores the authoritative state and retry bookkeeping.
        JobState state = saveFormatVersion >= 3
            ? dto.State
            : dto.IsCompleted ? JobState.Completed
                : dto.IsCancelled ? JobState.Cancelled
                : JobState.Queued;
        job.RestoreLifecycle(
            state,
            saveFormatVersion >= 3 ? dto.FailedAttempts : 0,
            saveFormatVersion >= 3 ? dto.LastFailureReason : null,
            saveFormatVersion >= 3 ? dto.BlockedReason : null,
            saveFormatVersion >= 3 ? dto.RetryNotBeforeSimTime : 0.0);
        job.RestoreReservedMaterials(
            saveFormatVersion >= 4 ? dto.ReservedMaterialIds : Array.Empty<Guid>());

        job.AssistanceItemTypes.AddRange(dto.AssistanceItemTypes);
        job.FailedAssistantCrewIds.AddRange(dto.FailedAssistantCrewIds);
        if (dto.TargetItemId.HasValue && itemsById.TryGetValue(dto.TargetItemId.Value, out var item))
            job.TargetItem = item;
        if (dto.Id != Guid.Empty)
            job.RestoreId(dto.Id);
        return job;
    }
}

public sealed class SimulationSaveDto
{
    // Version 0 identifies saves written before an explicit format version existed.
    public int SaveFormatVersion { get; set; }
    public double SimTime { get; set; }
    public double UpdateAccumulator { get; set; }
    public double CableCapacityKW { get; set; } = 50.0;
    public double CoolantCapacityLiters { get; set; } = 100.0;
    public double CoolantStoredLiters { get; set; } = 100.0;
    public double CoolantReserveLiters { get; set; } = 100.0;
    public double CoolantLeakRateLitersPerSecond { get; set; } = 0.25;
    public double PropellantPipeCapacityKgPerSecond { get; set; } = 5.0;
    public bool IsPaused { get; set; }
    public double TimeScale { get; set; } = 1.0;
    public double AtmosphereDiffusionRate { get; set; } = 6.0;
    public double AtmosphereVentingRate { get; set; } = 5.0;
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
    public double? PowerFraction { get; set; }
    public bool ProductionPaused { get; set; }
    public int ProductionLimit { get; set; } = -1;
    public int ProductionCyclesCompleted { get; set; }
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
    public int ComponentsProduced { get; set; }
    public double ComponentFabricationProgress { get; set; }
    public double ComponentFabricationCycle { get; set; } = 8.0;
    public bool IsComponentFabricationInProgress { get; set; }
    public int WaterRationsProduced { get; set; }
    public int FuelCanistersProduced { get; set; }
    public bool ProduceFuelCanisters { get; set; }
    public int MiningRange { get; set; } = 6;
    public double IceProcessingProgress { get; set; }
    public double IceProcessingCycle { get; set; } = 5.0;
    public bool IsIceProcessingInProgress { get; set; }
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
    public Guid? DeferredJobId { get; set; }
    public double DeferredJobTimer { get; set; }
    public bool IsRequestingAssistance { get; set; }
    public int ConsecutiveTaskFailures { get; set; }
    public bool IsRetreating { get; set; }
    public int? RetreatTargetX { get; set; }
    public int? RetreatTargetY { get; set; }
    public double RetreatRetryTimer { get; set; }
    public CrewDiagnosticSaveDto? LastDiagnostic { get; set; }
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
    public Guid? TargetCrewId { get; set; }
    public Guid? SourceTaskJobId { get; set; }
    public CrewDiagnosticReason? AssistanceReason { get; set; }
    public List<ItemType> AssistanceItemTypes { get; set; } = new();
    public List<Guid> FailedAssistantCrewIds { get; set; } = new();
    public int Priority { get; set; }
    public Guid? AssignedCrewId { get; set; }
    public double WorkRemaining { get; set; }
    public TileType? ConstructTileType { get; set; }
    public MachineType? ConstructMachineType { get; set; }
    public int RequiredSteelPlates { get; set; }
    public int RequiredMachineComponents { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }

    // Version 3 lifecycle fields. Defaults keep versions 0-3 readable.
    public JobState State { get; set; } = JobState.Queued;
    public int FailedAttempts { get; set; }
    public string? LastFailureReason { get; set; }
    public string? BlockedReason { get; set; }
    public double RetryNotBeforeSimTime { get; set; }

    // Version 4 material/tool reservations.
    public List<Guid> ReservedMaterialIds { get; set; } = new();
}

public sealed class ScenarioObjectiveSaveDto
{
    public string Id { get; set; } = "";
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
}

public sealed class CrewDiagnosticSaveDto
{
    public CrewDiagnosticReason Reason { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public int LastReachableX { get; set; }
    public int LastReachableY { get; set; }
    public int? BlockingX { get; set; }
    public int? BlockingY { get; set; }
    public string Detail { get; set; } = "";
    public int FailureCount { get; set; }
    public CrewDiagnosticReason? UnderlyingReason { get; set; }
    public bool PhysicalRouteExists { get; set; }
}
