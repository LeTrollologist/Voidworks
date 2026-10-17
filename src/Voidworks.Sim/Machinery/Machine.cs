using System;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Machinery;

public sealed class Machine
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public MachineType Type { get; }
    public GridCoord Position { get; set; }

    public bool IsOnline { get; set; } = true;
    public bool IsPowered { get; set; } = false;

    // Power characteristics (in kW)
    public double PowerProductionKW { get; set; }
    public double PowerConsumptionKW { get; set; }

    // Battery storage (in kWh)
    public double BatteryCapacityKWh { get; set; }
    public double BatteryStoredKWh { get; set; }

    // Active production/consumption rates (moles/second)
    public double O2GenerationRatePerSecond { get; set; } = 0.5;   // ~0.5 mol/s for oxygen generator
    public double CO2ScrubbingRatePerSecond { get; set; } = 0.25;  // ~0.25 mol/s for scrubber

    public Machine(MachineType type, GridCoord position, string? name = null)
    {
        Type = type;
        Position = position;
        Name = name ?? type.ToString();

        switch (type)
        {
            case MachineType.Reactor:
                PowerProductionKW = 25.0; // 25 kW nuclear reactor
                PowerConsumptionKW = 0.0;
                break;

            case MachineType.Battery:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 0.0;
                BatteryCapacityKWh = 100.0; // 100 kWh capacitor bank
                BatteryStoredKWh = 75.0;    // starts 75% charged
                break;

            case MachineType.OxygenGenerator:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 3.5;   // consumes 3.5 kW
                break;

            case MachineType.CarbonScrubber:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 2.0;   // consumes 2.0 kW
                break;
        }
    }

    public void Tick(ShipTile tile, double dt)
    {
        if (!IsOnline || !IsPowered || dt <= 0) return;

        switch (Type)
        {
            case MachineType.OxygenGenerator:
                // Produces oxygen into local room atmosphere
                if (!tile.IsVacuumSink)
                {
                    double o2Produced = O2GenerationRatePerSecond * dt;
                    tile.Atmosphere.Oxygen += o2Produced;
                }
                break;

            case MachineType.CarbonScrubber:
                // Scrubs CO2 from local room atmosphere
                if (!tile.IsVacuumSink && tile.Atmosphere.CarbonDioxide > 0)
                {
                    double co2ToScrub = Math.Min(tile.Atmosphere.CarbonDioxide, CO2ScrubbingRatePerSecond * dt);
                    tile.Atmosphere.CarbonDioxide -= co2ToScrub;
                }
                break;
        }
    }
}
