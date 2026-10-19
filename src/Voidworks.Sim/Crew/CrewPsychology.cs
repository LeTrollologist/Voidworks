using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Crew;

/// <summary>
/// Stable temperament and independently changing psychological pressures for one crew member.
/// </summary>
public sealed class CrewPsychology
{
    public double Courage { get; private set; }
    public double Discipline { get; private set; }
    public double SelfPreservation { get; private set; }
    public double Determination { get; private set; }
    public double Sociability { get; private set; }

    public double Motivation { get; private set; } = 50.0;
    public double Fear { get; private set; }
    public double Stress { get; private set; }
    public double Anger { get; private set; }
    public double Morale { get; private set; } = 70.0;

    public double HazardSensitivityMultiplier => Math.Clamp(
        1.0 + Fear * 0.007 + Stress * 0.0045 + SelfPreservation * 0.002 - Courage * 0.0015,
        0.7,
        2.5);

    public double HazardTolerance => Math.Clamp(
        18.0 + Courage * 0.12 + Determination * 0.08 - SelfPreservation * 0.08 - Fear * 0.12 - Stress * 0.1,
        3.0,
        35.0);

    public double MovementSpeedMultiplier => Math.Clamp(
        1.0 + (Motivation - 50.0) * 0.002 + Fear * 0.0005 - Stress * 0.001 - (100.0 - Morale) * 0.0005,
        0.7,
        1.2);

    public double WorkRateMultiplier => Math.Clamp(
        1.0 + (Motivation - 50.0) * 0.003 - Stress * 0.0015 - Fear * 0.001
        - Anger * 0.0005 - (100.0 - Morale) * 0.001,
        0.65,
        1.2);

    public double PersonalNeedThreshold => Math.Clamp(
        50.0 + Discipline * 0.1 + (Motivation - 50.0) * 0.1 - Stress * 0.15,
        40.0,
        70.0);

    public double AssistanceWillingness => Math.Clamp(
        Sociability * 0.65 + Discipline * 0.2 + Motivation * 0.15
        - Stress * 0.25 - Fear * 0.15 - Anger * 0.1,
        0.0,
        100.0);

    public CrewPsychology(string stableSeed)
    {
        uint state = StableHash(stableSeed);
        Courage = NextTrait(ref state, 35.0, 60.0);
        Discipline = NextTrait(ref state, 35.0, 60.0);
        SelfPreservation = NextTrait(ref state, 35.0, 60.0);
        Determination = NextTrait(ref state, 35.0, 60.0);
        Sociability = NextTrait(ref state, 35.0, 60.0);
    }

    public void Tick(CrewMember crew, ShipTile currentTile, bool hasActiveTask, int nearbyCrewCount, double dt)
    {
        if (dt <= 0.0) return;

        double environmentalRisk = GetEnvironmentalRisk(currentTile);
        bool protectedBySuit = crew.IsEquippedWithSuit && crew.HasUsableOxygenSupply;
        double perceivedRisk = protectedBySuit ? environmentalRisk * 0.1 : environmentalRisk;
        double physicalDistress = Math.Max(
            Math.Clamp((60.0 - crew.Physiology.Health) / 60.0, 0.0, 1.0),
            Math.Clamp((70.0 - crew.Physiology.OxygenSaturation) / 70.0, 0.0, 1.0));

        double fearRate = perceivedRisk * (5.0 + (100.0 - Courage) * 0.04)
            + physicalDistress * 4.0
            - (perceivedRisk < 0.05 && physicalDistress < 0.05 ? 3.0 : 0.0);
        Fear = Adjust(Fear, fearRate * dt);

        double unmetNeed = Math.Max(
            crew.Physiology.Hunger,
            Math.Max(crew.Physiology.Thirst, crew.Physiology.Fatigue));
        double workload = hasActiveTask ? 1.0 : 0.0;
        double socialSupport = nearbyCrewCount * (Sociability / 100.0) * 0.8;
        double stressRate = perceivedRisk * 5.0 + workload * 0.35 + unmetNeed * 0.012
            - (perceivedRisk < 0.05 ? 1.2 : 0.0) - socialSupport;
        Stress = Adjust(Stress, stressRate * dt);
        Anger = Math.Max(0.0, Anger - (1.0 + socialSupport) * dt);

        double targetMorale = 75.0 + (Sociability - 50.0) * 0.08
            - Stress * 0.28 - Fear * 0.18 - unmetNeed * 0.2
            + Math.Min(8.0, socialSupport * 4.0);
        Morale += (Math.Clamp(targetMorale, 0.0, 100.0) - Morale) * Math.Min(1.0, 0.12 * dt);
        Morale = Math.Clamp(Morale, 0.0, 100.0);

        double taskDrive = hasActiveTask ? 67.0 : 48.0;
        double targetMotivation = taskDrive + Determination * 0.12 + Discipline * 0.08
            + (Morale - 50.0) * 0.12 - Stress * 0.18 - Fear * 0.12 - unmetNeed * 0.18;
        Motivation += (Math.Clamp(targetMotivation, 0.0, 100.0) - Motivation) * Math.Min(1.0, 0.2 * dt);
        Motivation = Math.Clamp(Motivation, 0.0, 100.0);
    }

    public bool ShouldRetreat(CrewMember crew, ShipTile currentTile)
    {
        if (crew.IsEquippedWithSuit && crew.HasUsableOxygenSupply)
            return false;

        double risk = GetEnvironmentalRisk(currentTile);
        return risk >= 0.85 || (risk >= 0.35 && Fear >= 60.0 && SelfPreservation >= 50.0);
    }

    public bool IsHazardTolerable(double effectiveHazardCost, bool isVacuum, bool hasWorkingSuit)
    {
        if (isVacuum && !hasWorkingSuit)
            return false;
        return effectiveHazardCost <= HazardTolerance;
    }

    public void RegisterInjury(double damage)
    {
        if (damage <= 0.0) return;
        Fear = Adjust(Fear, damage * 0.5);
        Stress = Adjust(Stress, damage * 0.65);
        Anger = Adjust(Anger, damage * 0.25);
    }

    public void RegisterTaskFailure(int consecutiveFailures)
    {
        double repeatScale = Math.Max(1, consecutiveFailures);
        double persistence = 1.0 - Determination / 150.0;
        Stress = Adjust(Stress, (4.0 + repeatScale * 2.0) * persistence);
        Anger = Adjust(Anger, (3.0 + repeatScale * 2.0) * persistence);
    }

    public void RegisterJobInterrupted()
    {
        Stress = Adjust(Stress, 2.0);
        Anger = Adjust(Anger, 4.0);
    }

    public void RegisterHelpedAnotherCrewMember()
    {
        Morale = Adjust(Morale, 2.5);
        Motivation = Adjust(Motivation, 1.5);
    }

    public void RegisterReceivedAssistance()
    {
        Fear = Adjust(Fear, -2.0);
        Stress = Adjust(Stress, -7.0);
        Anger = Adjust(Anger, -3.0);
        Morale = Adjust(Morale, 5.0);
        Motivation = Adjust(Motivation, 3.0);
    }

    public void Restore(
        double courage,
        double discipline,
        double selfPreservation,
        double determination,
        double sociability,
        double motivation,
        double fear,
        double stress,
        double anger,
        double morale)
    {
        Courage = ClampValue(courage, 50.0);
        Discipline = ClampValue(discipline, 50.0);
        SelfPreservation = ClampValue(selfPreservation, 50.0);
        Determination = ClampValue(determination, 50.0);
        Sociability = ClampValue(sociability, 50.0);
        Motivation = ClampValue(motivation, 50.0);
        Fear = ClampValue(fear, 0.0);
        Stress = ClampValue(stress, 0.0);
        Anger = ClampValue(anger, 0.0);
        Morale = ClampValue(morale, 70.0);
    }

    private static double GetEnvironmentalRisk(ShipTile tile)
    {
        var atmosphere = tile.Atmosphere;
        double totalMoles = atmosphere.TotalMoles;
        double oxygenPartialPressure = totalMoles > 0.0
            ? atmosphere.PressureKPa * atmosphere.Oxygen / totalMoles
            : 0.0;
        double toxicFraction = totalMoles > 0.0 ? atmosphere.ToxicGas / totalMoles : 0.0;
        double co2Fraction = totalMoles > 0.0 ? atmosphere.CarbonDioxide / totalMoles : 0.0;

        double pressureRisk = tile.IsVacuumSink
            ? 1.0
            : Math.Clamp((70.0 - atmosphere.PressureKPa) / 70.0, 0.0, 1.0);
        double oxygenRisk = Math.Clamp((16.0 - oxygenPartialPressure) / 16.0, 0.0, 1.0);
        double toxicRisk = Math.Clamp((toxicFraction - 0.005) * 20.0, 0.0, 1.0);
        double co2Risk = Math.Clamp((co2Fraction - 0.05) * 10.0, 0.0, 1.0);
        double temperatureRisk = atmosphere.TemperatureK < 260.0
            ? Math.Clamp((260.0 - atmosphere.TemperatureK) / 120.0, 0.0, 1.0)
            : Math.Clamp((atmosphere.TemperatureK - 330.0) / 120.0, 0.0, 1.0);

        return Math.Max(pressureRisk,
            Math.Max(oxygenRisk, Math.Max(toxicRisk, Math.Max(co2Risk, temperatureRisk))));
    }

    private static double Adjust(double value, double delta)
        => Math.Clamp(value + delta, 0.0, 100.0);

    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }
        return hash == 0 ? 0x9E3779B9 : hash;
    }

    private static double NextTrait(ref uint state, double minimum, double range)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return minimum + state / (double)uint.MaxValue * range;
    }

    private static double ClampValue(double value, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, 0.0, 100.0) : fallback;
}
