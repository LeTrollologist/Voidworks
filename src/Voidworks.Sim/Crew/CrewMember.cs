using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim.Items;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Crew;

public sealed class CrewMember
{
    private readonly Dictionary<CrewSkill, int> _skills = new()
    {
        [CrewSkill.Construction] = CrewSkillRules.DefaultLevel,
        [CrewSkill.Repair] = CrewSkillRules.DefaultLevel,
        [CrewSkill.Logistics] = CrewSkillRules.DefaultLevel
    };

    private readonly Dictionary<CrewSkill, WorkPriority> _workPriorities = new()
    {
        [CrewSkill.Construction] = WorkPriority.Normal,
        [CrewSkill.Repair] = WorkPriority.Normal,
        [CrewSkill.Logistics] = WorkPriority.Normal
    };

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; set; }
    public GridCoord Position { get; set; }
    public string Role { get; set; } = "Generalist";
    public GridCoord? AssignedBunkCoord { get; set; }
    public Physiology Physiology { get; } = new();
    public CrewPsychology Psychology { get; }

    public bool IsEquippedWithSuit { get; set; }
    public Item? EquippedOxygenTank { get; set; }
    public List<Item> Inventory { get; } = new();
    public bool HasUsableOxygenSupply => EquippedOxygenTank is { StoredOxygenMoles: > 0.05 };
    public bool CanSafelyTraverseVacuum => IsEquippedWithSuit && HasUsableOxygenSupply;

    // Movement speed: tiles per second (2.0 tiles/s default)
    public double MoveSpeed { get; set; } = 2.0;
    public double EffectiveMoveSpeed
    {
        get
        {
            var physiology = Physiology;
            double conditionPenalty = 0.5 * Math.Clamp(physiology.Fatigue / 100.0, 0.0, 1.0)
                + 0.25 * Math.Clamp((100.0 - physiology.Health) / 100.0, 0.0, 1.0)
                + 0.25 * Math.Clamp((100.0 - physiology.OxygenSaturation) / 100.0, 0.0, 1.0)
                + 0.1 * Math.Clamp(physiology.Thirst / 100.0, 0.0, 1.0)
                + 0.1 * Math.Clamp(physiology.Hunger / 100.0, 0.0, 1.0);
            return Math.Max(0.1, MoveSpeed * (1.0 - Math.Min(0.8, conditionPenalty)) * Psychology.MovementSpeedMultiplier);
        }
    }

    public CrewMember(string name, GridCoord initialPosition, string role = "Generalist")
    {
        Name = name;
        Position = initialPosition;
        Role = role;
        Psychology = new CrewPsychology($"{name}|{role}|{initialPosition.X}|{initialPosition.Y}");
    }

    internal void RestoreId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Crew identity must not be empty.", nameof(id));
        Id = id;
    }

    public bool HasItem(ItemType type) => Inventory.Any(i => i.Type == type);
    public Item? GetItem(ItemType type) => Inventory.FirstOrDefault(i => i.Type == type);

    public int GetSkill(CrewSkill skill) => _skills[skill];

    public void SetSkill(CrewSkill skill, int level)
    {
        if (!Enum.IsDefined(skill))
            throw new ArgumentException($"Unknown crew skill value {(int)skill}.", nameof(skill));
        _skills[skill] = CrewSkillRules.ClampLevel(level);
    }

    /// <summary>Work-rate multiplier applied on top of physiology and psychology.</summary>
    public double GetSkillMultiplier(CrewSkill skill) => CrewSkillRules.WorkSpeedMultiplier(_skills[skill]);

    public WorkPriority GetWorkPriority(CrewSkill skill) => _workPriorities[skill];

    public void SetWorkPriority(CrewSkill skill, WorkPriority priority)
    {
        if (!Enum.IsDefined(skill))
            throw new ArgumentException($"Unknown crew skill value {(int)skill}.", nameof(skill));
        if (!Enum.IsDefined(priority))
            throw new ArgumentException($"Unknown work priority value {(int)priority}.", nameof(priority));
        _workPriorities[skill] = priority;
    }

    /// <summary>Job-selection score adjustment from this crew member's preferences.</summary>
    public int GetPriorityBias(CrewSkill skill) => CrewSkillRules.SelectionBias(_workPriorities[skill]);

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

    public bool EquipReplacementOxygenTank(Item tank)
    {
        if (!IsEquippedWithSuit || tank.Type != ItemType.OxygenTank || !Inventory.Contains(tank))
            return false;

        Inventory.Remove(tank);
        EquippedOxygenTank = tank;
        return true;
    }

    public bool Eat(Item food)
    {
        if (food.Type != ItemType.FoodRation) return false;
        Physiology.ConsumeFood(food.NutritionValue > 0 ? food.NutritionValue : 40.0);
        Inventory.Remove(food);
        return true;
    }

    public bool Drink(Item water)
    {
        if (water.Type != ItemType.WaterRation) return false;
        Physiology.ConsumeWater(water.HydrationValue > 0 ? water.HydrationValue : 50.0);
        Inventory.Remove(water);
        return true;
    }

    public void Rest(double dt)
    {
        Physiology.RecoverFatigue(15.0 * dt);
    }
}
