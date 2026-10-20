using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Jobs;

public sealed class Job
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; set; }
    public JobType Type { get; }
    public GridCoord TargetCoord { get; set; }
    public Item? TargetItem { get; set; }
    public ItemType? DesiredItemType { get; set; }
    public int Priority { get; set; } // Higher number = higher priority
    public Guid? AssignedCrewId { get; set; }
    public Guid? TargetCrewId { get; set; }
    public Guid? SourceTaskJobId { get; set; }
    public CrewDiagnosticReason? AssistanceReason { get; set; }
    public List<ItemType> AssistanceItemTypes { get; } = new();
    public List<Guid> FailedAssistantCrewIds { get; } = new();
    public double WorkRemainingSeconds { get; set; }
    public TileType? ConstructTileType { get; set; }
    public MachineType? ConstructMachineType { get; set; }
    public int RequiredSteelPlates { get; set; }
    public int RequiredMachineComponents { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }

    public Job(JobType type, GridCoord targetCoord, int priority = 0, double workSeconds = 0.0, string? name = null)
    {
        Type = type;
        TargetCoord = targetCoord;
        Priority = priority;
        WorkRemainingSeconds = workSeconds;
        Name = name ?? type.ToString();
    }

    internal void RestoreId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Job identity must not be empty.", nameof(id));
        Id = id;
    }

    public static Job CreateBreachRepair(GridCoord breachCoord)
    {
        return new Job(JobType.RepairBreach, breachCoord, priority: 100, workSeconds: 3.0, $"Repair Breach at {breachCoord}");
    }

    public static Job CreateFetchItem(Item item, int priority = 10)
    {
        return new Job(JobType.FetchItem, item.Position, priority: priority, workSeconds: 0.2, $"Fetch {item.Name}")
        {
            TargetItem = item,
            DesiredItemType = item.Type
        };
    }

    public static Job CreateEatFood(Item foodItem, int priority = 80)
    {
        return new Job(JobType.EatFood, foodItem.Position, priority: priority, workSeconds: 1.0, $"Eat {foodItem.Name}")
        {
            TargetItem = foodItem,
            DesiredItemType = ItemType.FoodRation
        };
    }

    public static Job CreateDrinkWater(Item waterItem, int priority = 85)
    {
        return new Job(JobType.DrinkWater, waterItem.Position, priority: priority, workSeconds: 0.5, $"Drink {waterItem.Name}")
        {
            TargetItem = waterItem,
            DesiredItemType = ItemType.WaterRation
        };
    }

    public static Job CreateRest(GridCoord bunkCoord, int priority = 75, double restSeconds = 5.0)
    {
        return new Job(JobType.Rest, bunkCoord, priority: priority, workSeconds: restSeconds, $"Rest in Bunk at {bunkCoord}");
    }

    public static Job CreateRefineOre(GridCoord smelterCoord, Item ironOre, int priority = 40)
    {
        return new Job(JobType.RefineOre, smelterCoord, priority: priority, workSeconds: 2.0, "Deliver Ore to Smelter")
        {
            TargetItem = ironOre,
            DesiredItemType = ItemType.IronOre
        };
    }

    public static Job CreateDeliverItem(GridCoord destination, Item item, int priority = 35)
    {
        return new Job(JobType.DeliverItem, destination, priority, workSeconds: 0.0,
            $"Deliver {item.Name}")
        {
            TargetItem = item,
            DesiredItemType = item.Type
        };
    }

    public static Job CreateBuildTile(GridCoord target, TileType tileType, int steelPlates, double workSeconds)
    {
        if (tileType is not (TileType.HullWall or TileType.InteriorWall or TileType.Floor or TileType.Door))
            throw new ArgumentOutOfRangeException(nameof(tileType), "Only ship structure tiles can be constructed.");
        if (steelPlates < 0 || workSeconds < 0.0 || !double.IsFinite(workSeconds))
            throw new ArgumentOutOfRangeException(nameof(steelPlates));

        return new Job(JobType.Construct, target, priority: 30, workSeconds,
            $"Construct {tileType} at {target}")
        {
            ConstructTileType = tileType,
            RequiredSteelPlates = steelPlates
        };
    }

    public static Job CreateInstallMachine(
        GridCoord target,
        MachineType machineType,
        int steelPlates,
        int machineComponents,
        double workSeconds)
    {
        if (steelPlates < 0 || machineComponents < 0 || workSeconds < 0.0 || !double.IsFinite(workSeconds))
            throw new ArgumentOutOfRangeException(nameof(steelPlates));

        return new Job(JobType.Construct, target, priority: 30, workSeconds,
            $"Install {machineType} at {target}")
        {
            ConstructMachineType = machineType,
            RequiredSteelPlates = steelPlates,
            RequiredMachineComponents = machineComponents
        };
    }

    public bool IsConstructionOrder => Type == JobType.Construct &&
        (ConstructTileType.HasValue ^ ConstructMachineType.HasValue);

    public static Job CreateAssistCrew(
        CrewMember requester,
        Job? sourceTask,
        CrewDecisionDiagnostic diagnostic,
        IEnumerable<ItemType>? requiredItems = null)
    {
        CrewDiagnosticReason reason = diagnostic.Reason == CrewDiagnosticReason.RepeatedTaskFailure
            ? diagnostic.UnderlyingReason ?? CrewDiagnosticReason.RepeatedTaskFailure
            : diagnostic.Reason;
        GridCoord target = reason == CrewDiagnosticReason.BlockedByLockedDoor && diagnostic.BlockingTile.HasValue
            ? diagnostic.BlockingTile.Value
            : requester.Position;
        var job = new Job(
            JobType.AssistCrew,
            target,
            priority: 120,
            workSeconds: 0.0,
            $"Assist {requester.Name}: {reason}")
        {
            TargetCrewId = requester.Id,
            SourceTaskJobId = sourceTask?.Id,
            AssistanceReason = reason
        };
        if (requiredItems != null)
            job.AssistanceItemTypes.AddRange(requiredItems.Distinct());
        return job;
    }
}
