using System;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Crew;

public sealed class Physiology
{
    // Moles of O2 consumed per second at rest (~0.00035 mol/s = ~0.84 L/min at STP)
    public const double BaseO2ConsumptionPerSecond = 0.0005;

    public double Health { get; set; } = 100.0;
    public double OxygenSaturation { get; set; } = 100.0; // 0 - 100%
    public double Hunger { get; set; } = 0.0;             // 0 (full) - 100 (starving)
    public double Thirst { get; set; } = 0.0;             // 0 (hydrated) - 100 (parched)
    public double Fatigue { get; set; } = 0.0;            // 0 (rested) - 100 (exhausted)

    public bool IsAlive => Health > 0.0;
    public bool IsIncapacitated => Health < 20.0 || OxygenSaturation < 30.0;

    public void Tick(CrewMember crew, ShipTile currentTile, double dt)
    {
        if (!IsAlive || dt <= 0) return;

        // 1. Oxygen consumption and respiration
        double o2Needed = BaseO2ConsumptionPerSecond * dt;
        bool isBreathing = false;

        // Check if breathing from suit tank
        if (crew.IsEquippedWithSuit && crew.EquippedOxygenTank != null && crew.EquippedOxygenTank.StoredOxygenMoles > 0)
        {
            double consumed = Math.Min(crew.EquippedOxygenTank.StoredOxygenMoles, o2Needed);
            crew.EquippedOxygenTank.StoredOxygenMoles -= consumed;
            isBreathing = true;
        }
        else
        {
            // Breathing ambient air
            if (!currentTile.IsVacuumSink && currentTile.Atmosphere.Oxygen >= o2Needed && currentTile.Atmosphere.PressureKPa >= 50.0)
            {
                // Inhale O2, exhale CO2
                currentTile.Atmosphere.Oxygen -= o2Needed;
                currentTile.Atmosphere.CarbonDioxide += o2Needed;
                isBreathing = true;
            }
        }

        // 2. Oxygen saturation dynamics
        if (isBreathing)
        {
            OxygenSaturation = Math.Min(100.0, OxygenSaturation + (10.0 * dt));
        }
        else
        {
            // Suffocating
            OxygenSaturation = Math.Max(0.0, OxygenSaturation - (12.0 * dt));
            if (OxygenSaturation < 50.0)
            {
                // Hypoxia damage
                Health = Math.Max(0.0, Health - (8.0 * dt));
            }
        }

        // 3. Vacuum decompression damage if unsuited in vacuum
        if (!crew.IsEquippedWithSuit && (currentTile.IsVacuumSink || currentTile.Atmosphere.PressureKPa < 20.0))
        {
            // Rapid explosive decompression trauma
            Health = Math.Max(0.0, Health - (15.0 * dt));
        }

        // 4. Gradual hunger, thirst, and fatigue
        Hunger = Math.Min(100.0, Hunger + (0.02 * dt));
        Thirst = Math.Min(100.0, Thirst + (0.04 * dt));
        Fatigue = Math.Min(100.0, Fatigue + (0.03 * dt));

        // Damage from extreme starvation or dehydration
        if (Hunger >= 100.0)
        {
            Health = Math.Max(0.0, Health - (1.0 * dt));
        }
        if (Thirst >= 100.0)
        {
            Health = Math.Max(0.0, Health - (2.0 * dt));
        }
    }

    public void ConsumeFood(double nutrition)
    {
        Hunger = Math.Max(0.0, Hunger - nutrition);
    }

    public void ConsumeWater(double hydration)
    {
        Thirst = Math.Max(0.0, Thirst - hydration);
    }

    public void RecoverFatigue(double amount)
    {
        Fatigue = Math.Max(0.0, Fatigue - amount);
    }
}
