using System;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Machinery;

public sealed class Machine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; set; }
    public MachineType Type { get; }
    public GridCoord Position { get; set; }

    public bool IsOnline { get; set; } = true;
    private bool _isPowered;
    public bool IsPowered
    {
        get => _isPowered;
        set
        {
            _isPowered = value;
            PowerFraction = value ? 1.0 : 0.0;
        }
    }

    /// <summary>Fraction of requested electrical power currently available, from 0 to 1.</summary>
    public double PowerFraction { get; private set; }

    // Power characteristics (in kW)
    public double PowerProductionKW { get; set; }
    public double RatedPowerProductionKW { get; set; }
    public double PowerConsumptionKW { get; set; }

    // Battery storage (in kWh)
    public double BatteryCapacityKWh { get; set; }
    public double BatteryStoredKWh { get; set; }
    public double BatteryMaxChargeKW { get; set; } = 50.0;
    public double BatteryMaxDischargeKW { get; set; } = 50.0;

    // Life support rates (moles/second)
    public double O2GenerationRatePerSecond { get; set; } = 0.5;   // ~0.5 mol/s for oxygen generator
    public double CO2ScrubbingRatePerSecond { get; set; } = 0.25;  // ~0.25 mol/s for scrubber

    // Thermal / Reactor & Radiator properties
    public double CoreTemperatureKelvin { get; set; } = 350.0;
    public double HeatGenerationKW { get; set; } = 12.0;
    public double HeatCapacityJoulesPerKelvin { get; set; } = 100_000.0;
    public double MaxSafeTemperatureKelvin { get; set; } = 1000.0;
    public double ScramTemperatureKelvin { get; set; } = 1200.0;
    public double ScramResetTemperatureKelvin { get; set; } = 600.0;
    public bool IsScrammed { get; set; } = false;
    public bool CanResetScram => CoreTemperatureKelvin <= ScramResetTemperatureKelvin;
    public double RadiatorCoolingKW { get; set; } = 15.0;
    public double CoolantPumpFlowRateLitersPerSecond { get; set; } = 0.5;

    public bool TryResetScram()
    {
        if (!IsScrammed) return true;
        if (!CanResetScram) return false;

        IsScrammed = false;
        IsOnline = true;
        PowerProductionKW = RatedPowerProductionKW;
        return true;
    }

    // Mining Laser
    public GridCoord? TargetAsteroidCoord { get; set; }
    public double MiningProgressSeconds { get; set; } = 0.0;
    public double MiningCycleSeconds { get; set; } = 3.0;
    public int TotalOreExtracted { get; set; } = 0;

    // Smelter Refinery
    public int IronOreStored { get; set; } = 0;
    public int SteelPlatesProduced { get; set; } = 0;
    public double SmeltProgressSeconds { get; set; } = 0.0;
    public double SmeltCycleSeconds { get; set; } = 4.0;

    // Hydroponics Bay
    public double WaterStoredLiters { get; set; } = 10.0;
    public int FoodRationsProduced { get; set; } = 0;
    public double GrowthProgressSeconds { get; set; } = 0.0;
    public double GrowthCycleSeconds { get; set; } = 10.0;

    // Propulsion & Fuel Tank
    public double FuelStoredKg { get; set; } = 0.0;
    public double FuelCapacityKg { get; set; } = 0.0;
    public bool IsFiring { get; set; } = false;
    public double ThrustKN { get; set; } = 50.0;
    public double FuelBurnRateKgPerSecond { get; set; } = 2.5;

    public Machine(MachineType type, GridCoord position, string? name = null)
    {
        Type = type;
        Position = position;
        Name = name ?? type.ToString();

        switch (type)
        {
            case MachineType.Reactor:
                PowerProductionKW = 25.0;
                RatedPowerProductionKW = PowerProductionKW;
                PowerConsumptionKW = 0.0;
                CoreTemperatureKelvin = 350.0;
                HeatGenerationKW = 12.0;
                break;

            case MachineType.Battery:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 0.0;
                BatteryCapacityKWh = 100.0;
                BatteryStoredKWh = 75.0;
                break;

            case MachineType.OxygenGenerator:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 3.5;
                break;

            case MachineType.CarbonScrubber:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 2.0;
                break;

            case MachineType.MiningLaser:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 5.0;
                break;

            case MachineType.SmelterRefinery:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 6.0;
                break;

            case MachineType.HydroponicsBay:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 2.0;
                WaterStoredLiters = 15.0;
                break;

            case MachineType.Radiator:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 0.5; // pump power
                RadiatorCoolingKW = 15.0;
                CoolantPumpFlowRateLitersPerSecond = 0.5;
                break;

            case MachineType.FuelTank:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 0.0;
                FuelCapacityKg = 500.0;
                FuelStoredKg = 400.0;
                break;

            case MachineType.MainEngine:
                PowerProductionKW = 0.0;
                PowerConsumptionKW = 1.0; // gimbal & igniter
                ThrustKN = 50.0;
                FuelBurnRateKgPerSecond = 2.5;
                break;
        }
    }

    internal void SetPowerFraction(double fraction)
    {
        PowerFraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0.0, 1.0) : 0.0;
        _isPowered = PowerFraction > 0.0;
    }

    internal void RestoreId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Machine identity must not be empty.", nameof(id));
        Id = id;
    }

    public void Tick(ShipTile tile, double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;

        // If machine is reactor, track thermal physics even if not actively powered by external grid
        if (Type == MachineType.Reactor)
        {
            TickReactorThermal(dt);
        }

        if (!IsOnline || !IsPowered) return;

        double poweredDt = dt * PowerFraction;
        if (poweredDt <= 0.0) return;

        switch (Type)
        {
            case MachineType.OxygenGenerator:
                if (!tile.IsVacuumSink)
                {
                    tile.Atmosphere.Oxygen += O2GenerationRatePerSecond * poweredDt;
                }
                break;

            case MachineType.CarbonScrubber:
                if (!tile.IsVacuumSink && tile.Atmosphere.CarbonDioxide > 0)
                {
                    double co2ToScrub = Math.Min(tile.Atmosphere.CarbonDioxide, CO2ScrubbingRatePerSecond * poweredDt);
                    tile.Atmosphere.CarbonDioxide -= co2ToScrub;
                }
                break;

            case MachineType.SmelterRefinery:
                TickSmelter(poweredDt);
                break;

            case MachineType.HydroponicsBay:
                TickHydroponics(tile, poweredDt);
                break;
        }
    }

    private void TickReactorThermal(double dt)
    {
        if (IsOnline)
        {
            // Generating heat (P = HeatGenerationKW * 1000 W)
            double heatJoules = HeatGenerationKW * 1000.0 * dt;
            CoreTemperatureKelvin += heatJoules / HeatCapacityJoulesPerKelvin;

            // Meltdown / emergency SCRAM protection
            if (CoreTemperatureKelvin >= ScramTemperatureKelvin)
            {
                IsScrammed = true;
                IsOnline = false; // Emergency trip!
                PowerProductionKW = 0.0;
            }
        }
        else
        {
            // Passive radiation cooling down towards ambient ~293 K
            double ambient = 293.15;
            if (CoreTemperatureKelvin > ambient)
            {
                double coolRate = 0.5 * (CoreTemperatureKelvin - ambient) * dt;
                CoreTemperatureKelvin = Math.Max(ambient, CoreTemperatureKelvin - (coolRate / HeatCapacityJoulesPerKelvin));
            }
        }
    }

    private void TickSmelter(double dt)
    {
        if (IronOreStored > 0)
        {
            SmeltProgressSeconds += dt;
            if (SmeltProgressSeconds >= SmeltCycleSeconds)
            {
                SmeltProgressSeconds = 0.0;
                IronOreStored--;
                SteelPlatesProduced++;
            }
        }
    }

    private void TickHydroponics(ShipTile tile, double dt)
    {
        if (WaterStoredLiters <= 0.0 || tile.IsVacuumSink)
            return;

        const double waterUseLitersPerSecond = 0.02;
        const double co2UseMolesPerSecond = 0.05;
        double waterConsumed = Math.Min(WaterStoredLiters, waterUseLitersPerSecond * dt);
        WaterStoredLiters -= waterConsumed;

        // Photosynthesis consumes CO2 and releases the same number of oxygen moles.
        double co2Consumed = Math.Min(tile.Atmosphere.CarbonDioxide, co2UseMolesPerSecond * dt);
        if (co2Consumed > 0.0)
        {
            tile.Atmosphere.CarbonDioxide -= co2Consumed;
            tile.Atmosphere.Oxygen += co2Consumed;
        }

        // Crops only grow while supplied with both water and CO2.
        if (waterConsumed > 0.0 && co2Consumed > 0.0)
        {
            double waterSupportedSeconds = waterConsumed / waterUseLitersPerSecond;
            double co2SupportedSeconds = co2Consumed / co2UseMolesPerSecond;
            GrowthProgressSeconds += Math.Min(dt, Math.Min(waterSupportedSeconds, co2SupportedSeconds));
        }

        while (GrowthProgressSeconds >= GrowthCycleSeconds)
        {
            GrowthProgressSeconds -= GrowthCycleSeconds;
            FoodRationsProduced++;
        }
    }
}
