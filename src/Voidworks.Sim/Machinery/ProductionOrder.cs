using System;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Machinery;

public enum ProductionRecipe
{
    MineOre,
    SmeltSteel,
    FabricateComponent,
    ProcessWater,
    ProcessFuel,
    GrowFood
}

/// <summary>One queued production order. Quantity counts remaining cycles.</summary>
public sealed class ProductionOrder
{
    public ProductionRecipe Recipe { get; }
    public int Quantity { get; internal set; }

    public ProductionOrder(ProductionRecipe recipe, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Recipe = recipe;
        Quantity = quantity;
    }
}
