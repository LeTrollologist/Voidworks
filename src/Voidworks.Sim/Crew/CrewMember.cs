using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Items;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Crew;

public sealed class CrewMember
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public GridCoord Position { get; set; }
    public Physiology Physiology { get; } = new();

    public bool IsEquippedWithSuit { get; set; }
    public Item? EquippedOxygenTank { get; set; }
    public List<Item> Inventory { get; } = new();

    // Movement speed: tiles per second (2.0 tiles/s default)
    public double MoveSpeed { get; set; } = 2.0;

    public CrewMember(string name, GridCoord initialPosition)
    {
        Name = name;
        Position = initialPosition;
    }

    public bool HasItem(ItemType type) => Inventory.Any(i => i.Type == type);
    public Item? GetItem(ItemType type) => Inventory.FirstOrDefault(i => i.Type == type);

    public bool PickUp(Item item)
    {
        if (item.IsCarried) return false;
        item.IsCarried = true;
        Inventory.Add(item);
        return true;
    }

    public bool Drop(Item item, GridCoord dropPosition)
    {
        if (!Inventory.Remove(item)) return false;
        item.IsCarried = false;
        item.Position = dropPosition;
        return true;
    }

    public bool EquipSuit(Item suit, Item tank)
    {
        if (suit.Type != ItemType.EvaSuit || tank.Type != ItemType.OxygenTank)
            return false;

        Inventory.Remove(suit);
        Inventory.Remove(tank);

        IsEquippedWithSuit = true;
        EquippedOxygenTank = tank;
        return true;
    }
}
