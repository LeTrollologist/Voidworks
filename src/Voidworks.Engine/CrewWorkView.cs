using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;

namespace Voidworks.Engine;

/// <summary>
/// Shared presentation for crew proficiency and work preferences, used by both the
/// management roster and the crew inspector.
/// </summary>
public static class CrewWorkView
{
    public static void RenderSkills(CrewMember crew)
    {
        ImGui.TextColored(new float4(0.55f, 0.85f, 1.0f, 1.0f),
            $"Skills: build {crew.GetSkill(CrewSkill.Construction)}  " +
            $"repair {crew.GetSkill(CrewSkill.Repair)}  " +
            $"logistics {crew.GetSkill(CrewSkill.Logistics)}");
    }

    public static void RenderPriorities(
        SimulationState sim,
        CrewMember crew,
        Action<string> report,
        bool compact = false)
    {
        ImGui.TextDisabled(compact ? "Priorities (click to cycle):" : "Work priorities (click to cycle):");
        bool first = true;
        foreach (CrewSkill skill in Enum.GetValues<CrewSkill>())
        {
            if (!first) ImGui.SameLine();
            first = false;

            WorkPriority current = crew.GetWorkPriority(skill);
            string label = $"{ShortName(skill)}: {CrewSkillRules.DisplayName(current)}";
            string id = $"##{skill}-{crew.Id}";
            if (compact ? ImGui.SmallButton(label + id) : ImGui.Button(label + id))
            {
                WorkPriority next = Next(current);
                var result = sim.ExecuteCommand(new SetCrewWorkPriorityCommand(crew.Id, skill, next));
                report(result.Success
                    ? result.Message
                    : $"Rejected ({result.FailureCode}): {result.Message}");
            }
        }
    }

    public static WorkPriority Next(WorkPriority priority) => priority switch
    {
        WorkPriority.Low => WorkPriority.Normal,
        WorkPriority.Normal => WorkPriority.High,
        WorkPriority.High => WorkPriority.Urgent,
        _ => WorkPriority.Low
    };

    private static string ShortName(CrewSkill skill) => skill switch
    {
        CrewSkill.Construction => "Build",
        CrewSkill.Repair => "Repair",
        CrewSkill.Logistics => "Haul",
        _ => skill.ToString()
    };
}
