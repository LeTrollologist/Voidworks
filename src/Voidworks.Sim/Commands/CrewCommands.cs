using System;
using System.Linq;
using Voidworks.Sim.Crew;

namespace Voidworks.Sim.Commands;

/// <summary>
/// Sets how strongly a crew member prefers a category of routine work. The
/// preference biases job selection between routine jobs only; emergencies
/// (priority 100 and above) and personal survival needs always outrank it.
/// </summary>
public sealed record SetCrewWorkPriorityCommand(
    Guid CrewId,
    CrewSkill Skill,
    WorkPriority Priority) : ISimulationCommand
{
    public string Name => "SetCrewWorkPriority";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        var agent = simulation.Crew.FirstOrDefault(candidate => candidate.Crew.Id == CrewId);
        if (agent == null)
            return SimulationCommandResult.Fail("CrewNotFound", $"No crew member with id {CrewId} is aboard.");
        if (!Enum.IsDefined(Skill))
            return SimulationCommandResult.Fail("UnknownSkill", $"Unknown work category {(int)Skill}.");
        if (!CrewSkillRules.IsDefined(Priority))
            return SimulationCommandResult.Fail("InvalidPriority",
                $"Work priority must be between {(int)WorkPriority.Low} and {(int)WorkPriority.Urgent}.");

        agent.Crew.SetWorkPriority(Skill, Priority);
        return SimulationCommandResult.Ok(
            $"{agent.Crew.Name} now treats {CrewSkillRules.DisplayName(Skill)} work as " +
            $"{CrewSkillRules.DisplayName(Priority).ToLowerInvariant()} priority.");
    }
}
