namespace Voidworks.Sim.Crew;

/// <summary>
/// Work disciplines a crew member can be proficient in. Each routine job maps to
/// exactly one discipline, so proficiency changes how fast the work is done and
/// work priorities bias which category of work the crew member picks first.
/// </summary>
public enum CrewSkill
{
    Construction = 0,
    Repair = 1,
    Logistics = 2
}

/// <summary>
/// Per-crew preference for a work discipline. <see cref="Normal"/> is neutral;
/// lower values make a crew member avoid that category and higher values make
/// them seek it out when routine jobs compete.
/// </summary>
public enum WorkPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public static class CrewSkillRules
{
    public const int MinLevel = 1;
    public const int MaxLevel = 10;
    public const int DefaultLevel = 5;

    /// <summary>Job-selection score change per priority step away from normal.</summary>
    public const int PriorityBiasPerStep = 4;

    /// <summary>A level-5 specialist works at the crew's unmodified rate.</summary>
    public const double MultiplierAtDefault = 1.0;

    public static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

    public static double WorkSpeedMultiplier(int level)
        => 0.75 + 0.05 * ClampLevel(level);

    public static int SelectionBias(WorkPriority priority)
        => ((int)priority - (int)WorkPriority.Normal) * PriorityBiasPerStep;

    public static bool IsDefined(WorkPriority priority)
        => priority is >= WorkPriority.Low and <= WorkPriority.Urgent;

    public static string DisplayName(CrewSkill skill) => skill switch
    {
        CrewSkill.Construction => "Construction",
        CrewSkill.Repair => "Repair",
        CrewSkill.Logistics => "Logistics",
        _ => skill.ToString()
    };

    public static string DisplayName(WorkPriority priority) => priority switch
    {
        WorkPriority.Low => "Low",
        WorkPriority.Normal => "Normal",
        WorkPriority.High => "High",
        WorkPriority.Urgent => "Urgent",
        _ => priority.ToString()
    };
}
