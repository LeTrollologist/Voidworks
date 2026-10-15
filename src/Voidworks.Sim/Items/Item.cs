using System;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Items;

public sealed class Item
{
    public Guid Id { get; } = Guid.NewGuid();
    public ItemType Type { get; }
    public string Name { get; }
    public double WeightKg { get; set; }
    public GridCoord Position { get; set; }
    public bool IsCarried { get; set; }

    // Special properties (e.g. for oxygen tanks)
    public double StoredOxygenMoles { get; set; }

    public Item(ItemType type, GridCoord position, double weightKg = 1.0, string? name = null)
    {
        Type = type;
        Position = position;
        WeightKg = weightKg;
        Name = name ?? type.ToString();

        if (type == ItemType.OxygenTank)
        {
            StoredOxygenMoles = 50.0; // standard full tank (~hours of breathing)
        }
    }
}
