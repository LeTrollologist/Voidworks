using System;
using Voidworks.Sim.Items;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Jobs;

public sealed class Job
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public JobType Type { get; }
    public GridCoord TargetCoord { get; set; }
    public Item? TargetItem { get; set; }
    public ItemType? DesiredItemType { get; set; }
    public int Priority { get; set; } // Higher number = higher priority
    public Guid? AssignedCrewId { get; set; }
    public double WorkRemainingSeconds { get; set; }
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
}
