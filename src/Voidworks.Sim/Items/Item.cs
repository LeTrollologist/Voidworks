using System;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Items;

public sealed class Item
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public ItemType Type { get; }
    public string Name { get; }
    public double WeightKg { get; set; }
    public GridCoord Position { get; set; }
    public bool IsCarried { get; set; }
    public Guid? ReservedByCrewId { get; set; }
    public bool IsAvailable => !IsCarried && ReservedByCrewId == null;

    // Special properties (e.g. for oxygen tanks, food, water)
    public double StoredOxygenMoles { get; set; }
    public double NutritionValue { get; set; }
    public double HydrationValue { get; set; }

    public Item(ItemType type, GridCoord position, double? weightKg = null, string? name = null)
    {
        Type = type;
        Position = position;
        Name = name ?? type switch
        {
            ItemType.EvaSuit => "EVA Suit",
            ItemType.OxygenTank => "Oxygen Tank",
            ItemType.WeldingTorch => "Welding Torch",
            ItemType.SteelPlate => "Steel Plate",
            ItemType.IronOre => "Raw Iron Ore",
            ItemType.IceOre => "Water Ice Chunk",
            ItemType.WaterRation => "Water Pouch",
            ItemType.FoodRation => "Nutrient Paste Ration",
            ItemType.FuelCanister => "Propellant Canister",
            _ => type.ToString()
        };

        WeightKg = weightKg ?? type switch
        {
            ItemType.EvaSuit => 15.0,
            ItemType.OxygenTank => 8.0,
            ItemType.WeldingTorch => 3.0,
            ItemType.SteelPlate => 10.0,
            ItemType.IronOre => 12.0,
            ItemType.IceOre => 5.0,
            ItemType.WaterRation => 1.0,
            ItemType.FoodRation => 0.5,
            ItemType.FuelCanister => 20.0,
            _ => 1.0
        };

        if (type == ItemType.OxygenTank)
        {
            StoredOxygenMoles = 50.0; // standard full tank (~hours of breathing)
        }
        else if (type == ItemType.FoodRation)
        {
            NutritionValue = 40.0; // reduces hunger by 40%
        }
        else if (type == ItemType.WaterRation)
        {
            HydrationValue = 50.0;
        }
    }

    internal void RestoreId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Item identity must not be empty.", nameof(id));
        Id = id;
    }
}
