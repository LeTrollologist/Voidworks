namespace Voidworks.Sim.Navigation;

public readonly record struct PathHazardCosts(
    double Vacuum = 0.0,
    double LowPressure = 0.0,
    double LowOxygen = 0.0,
    double ToxicGas = 0.0,
    double CarbonDioxide = 0.0,
    double Temperature = 0.0)
{
    public double Total => Vacuum + LowPressure + LowOxygen + ToxicGas + CarbonDioxide + Temperature;

    public PathHazardCosts Scale(double multiplier) => new(
        Vacuum * multiplier,
        LowPressure * multiplier,
        LowOxygen * multiplier,
        ToxicGas * multiplier,
        CarbonDioxide * multiplier,
        Temperature * multiplier);

    public static PathHazardCosts operator +(PathHazardCosts left, PathHazardCosts right) => new(
        left.Vacuum + right.Vacuum,
        left.LowPressure + right.LowPressure,
        left.LowOxygen + right.LowOxygen,
        left.ToxicGas + right.ToxicGas,
        left.CarbonDioxide + right.CarbonDioxide,
        left.Temperature + right.Temperature);
}

public readonly record struct CrewPathDiagnostics(
    int Steps,
    double MovementCost,
    double DoorPenalty,
    PathHazardCosts HazardCosts,
    double RoleSensitivity,
    double ConditionMultiplier,
    double SuitProtection,
    bool HasWorkingSuit)
{
    public double TotalCost => MovementCost + DoorPenalty + HazardCosts.Total;
}
