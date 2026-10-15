using System;

namespace Voidworks.Sim.Atmosphere;

public sealed class GasMix
{
    public const double GasConstantR = 8.314462618; // J / (mol * K)
    public const double DefaultTileVolume = 2.5;     // cubic meters
    public const double StandardRoomTempK = 293.15;  // 20 degrees Celsius
    public const double StandardAtmPressureKPa = 101.325; // standard sea level pressure in kPa
    public const double CvMolar = 20.8;             // Heat capacity J / (mol * K) approx for diatomic gases

    public double Oxygen { get; set; }
    public double Nitrogen { get; set; }
    public double CarbonDioxide { get; set; }
    public double ToxicGas { get; set; }
    public double TemperatureK { get; set; }
    public double Volume { get; set; }

    public GasMix(double volume = DefaultTileVolume, double tempK = StandardRoomTempK)
    {
        Volume = volume;
        TemperatureK = tempK;
    }

    public double TotalMoles => Oxygen + Nitrogen + CarbonDioxide + ToxicGas;

    public double PressurePa
    {
        get
        {
            if (Volume <= 0 || TotalMoles <= 0) return 0.0;
            return (TotalMoles * GasConstantR * TemperatureK) / Volume;
        }
    }

    public double PressureKPa => PressurePa / 1000.0;

    public double OxygenPercentage => TotalMoles > 0 ? (Oxygen / TotalMoles) * 100.0 : 0.0;
    public double CarbonDioxidePercentage => TotalMoles > 0 ? (CarbonDioxide / TotalMoles) * 100.0 : 0.0;

    public static GasMix StandardAir(double volume = DefaultTileVolume, double pressureKPa = StandardAtmPressureKPa, double tempK = StandardRoomTempK)
    {
        var mix = new GasMix(volume, tempK);
        double totalMolesNeeded = (pressureKPa * 1000.0 * volume) / (GasConstantR * tempK);
        // Earth standard: ~20.9% O2, ~78.1% N2, ~0.04% CO2, remaining trace
        mix.Oxygen = totalMolesNeeded * 0.21;
        mix.Nitrogen = totalMolesNeeded * 0.79;
        return mix;
    }

    public static GasMix Vacuum(double volume = DefaultTileVolume)
    {
        return new GasMix(volume, 2.7); // 2.7K cosmic background or cold vacuum
    }

    public GasMix RemoveMoles(double molesToRemove)
    {
        double total = TotalMoles;
        var extracted = new GasMix(Volume, TemperatureK);

        if (total <= 0 || molesToRemove <= 0)
            return extracted;

        double fraction = Math.Min(1.0, molesToRemove / total);

        extracted.Oxygen = Oxygen * fraction;
        extracted.Nitrogen = Nitrogen * fraction;
        extracted.CarbonDioxide = CarbonDioxide * fraction;
        extracted.ToxicGas = ToxicGas * fraction;

        Oxygen -= extracted.Oxygen;
        Nitrogen -= extracted.Nitrogen;
        CarbonDioxide -= extracted.CarbonDioxide;
        ToxicGas -= extracted.ToxicGas;

        return extracted;
    }

    public void Add(GasMix incoming)
    {
        if (incoming == null || incoming.TotalMoles <= 0) return;

        double selfMoles = TotalMoles;
        double incMoles = incoming.TotalMoles;
        double newTotalMoles = selfMoles + incMoles;

        // Thermal equilibrium calculation:
        // U_total = (n1 * Cv * T1) + (n2 * Cv * T2)
        // T_final = U_total / (n_total * Cv) = (n1 * T1 + n2 * T2) / n_total
        if (newTotalMoles > 0)
        {
            TemperatureK = ((selfMoles * TemperatureK) + (incMoles * incoming.TemperatureK)) / newTotalMoles;
        }

        Oxygen += incoming.Oxygen;
        Nitrogen += incoming.Nitrogen;
        CarbonDioxide += incoming.CarbonDioxide;
        ToxicGas += incoming.ToxicGas;

        incoming.Oxygen = 0;
        incoming.Nitrogen = 0;
        incoming.CarbonDioxide = 0;
        incoming.ToxicGas = 0;
    }

    public GasMix Clone()
    {
        return new GasMix(Volume, TemperatureK)
        {
            Oxygen = Oxygen,
            Nitrogen = Nitrogen,
            CarbonDioxide = CarbonDioxide,
            ToxicGas = ToxicGas
        };
    }
}
