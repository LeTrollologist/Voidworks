using System;
using System.Collections.Generic;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Items;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Power;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

/// <summary>
/// Contextual, state-derived guidance for the active scenario. Hints deliberately
/// describe the next useful decision instead of prescribing a full solution.
/// </summary>
public static class ScenarioGuidance
{
    public const int MaxHints = 4;

    public static IReadOnlyList<string> Build(SimulationState sim)
    {
        var hints = new List<string>();
        if (sim.ActiveScenario is not { } scenario || scenario.IsCompleted || scenario.IsFailed)
            return hints;

        var breach = sim.Grid.AllTiles().FirstOrDefault(tile => tile.IsBreached);
        if (breach != null)
        {
            hints.Add($"Hull breach detected. Select the punctured tile and confirm the crew have EVA gear and a welding torch.");
        }

        var reactor = sim.Machines.FirstOrDefault(machine => machine.Type == MachineType.Reactor);
        var radiator = sim.Machines.FirstOrDefault(machine => machine.Type == MachineType.Radiator);
        if (reactor != null && radiator != null &&
            !sim.PlumbingSolver.IsCoolantLoopConnected(reactor.Position, radiator.Position) &&
            scenario.Objectives.Any(objective => objective.Id == "coolant-loop" && !objective.IsCompleted))
        {
            hints.Add($"Coolant loop severed between reactor {reactor.Position} and radiator {radiator.Position}. " +
                      "Select the intervening tiles and lay Coolant Pipe.");
        }

        if (radiator != null && !radiator.IsPowered && radiator.IsOnline)
        {
            hints.Add($"Radiator at {radiator.Position} is unpowered. Lay a power cable to the hull tile beside it so the coolant pump can run.");
        }

        var oxygen = sim.Machines.FirstOrDefault(machine => machine.Type == MachineType.OxygenGenerator);
        if (oxygen != null && scenario.Objectives.Any(objective => objective.Id == "life-support" && !objective.IsCompleted))
        {
            if (!oxygen.IsOnline)
                hints.Add("Oxygen generator is offline. Select it and switch it on once power is available.");
            else if (!oxygen.IsPowered)
                hints.Add("Oxygen generator is online but unpowered: " +
                          (PowerDiagnostics.ExplainMachinePower(sim, oxygen) ?? "no power is reaching it."));
        }

        if (sim.Machines.Count(machine => machine.Type == MachineType.Battery) < 2 &&
            scenario.Objectives.Any(objective => objective.Id == "auxiliary-power" && !objective.IsCompleted))
        {
            var plate = sim.JobManager.WorldItems
                .FirstOrDefault(item => item.Type == ItemType.SteelPlate && !item.IsCarried);
            string location = plate != null ? $" Materials are stockpiled at {plate.Position}." : string.Empty;
            hints.Add("Construction: select an unused floor tile and choose Build Battery (4 steel)." + location);
        }

        int unpowered = sim.Machines.Count(machine => machine.IsOnline && !machine.IsPowered &&
                                                     machine.Type is not (MachineType.Reactor or MachineType.Battery));
        if (unpowered > 0)
            hints.Add($"{unpowered} machine{(unpowered == 1 ? " is" : "s are")} unpowered. " +
                      "Open the Power Grid overlay and trace the cable run back to a reactor or charged battery.");

        var lowOxygen = sim.Crew
            .Select(agent => agent.Crew)
            .Where(crew => crew.Physiology.IsAlive && crew.Physiology.OxygenSaturation < 70.0)
            .OrderBy(crew => crew.Physiology.OxygenSaturation)
            .FirstOrDefault();
        if (lowOxygen != null)
        {
            hints.Add($"{lowOxygen.Name} oxygen is {lowOxygen.Physiology.OxygenSaturation:F0}%. " +
                      "Seal breaches and restore oxygen generation before the crew is incapacitated.");
        }

        if (scenario.TimeLimitSeconds.HasValue)
        {
            double remaining = scenario.TimeLimitSeconds.Value - scenario.ElapsedSeconds;
            if (remaining > 0.0 && remaining < 60.0)
                hints.Add($"Only {remaining:F0}s remain. Address the highest-risk objective first.");
        }

        if (hints.Count == 0 && sim.JobManager.ActiveJobs.Count == 0)
            hints.Add("Crew are idle. Review the job board, stockpiles, and machine inputs to find the next blocked task.");

        return hints.Take(MaxHints).ToArray();
    }
}
