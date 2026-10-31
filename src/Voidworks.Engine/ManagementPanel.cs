using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

/// <summary>
/// Management surface: crew roster, job controls, stockpile, and production state.
/// Read-only except for explicit job commands; selection always flows back into the
/// shared inspector selection so the world view and panels stay in sync.
/// </summary>
public sealed class ManagementPanel
{
    private readonly CrewRosterView _roster = new();
    private readonly StockpileView _stockpile = new();
    private readonly ProductionView _production = new();
    private string _status = string.Empty;

    public void Render(
        SimulationState sim,
        ref GridCoord? selectedTile,
        ref CrewMember? selectedCrew,
        ref Machine? selectedMachine)
    {
        ImGui.SetNextWindowPos(new float2(10f, 165f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(470f, 385f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Management"))
        {
            ImGui.End();
            return;
        }

        _roster.Render(sim, ref selectedCrew, ref selectedTile, ref selectedMachine);
        ImGui.Separator();
        RenderJobs(sim, ref selectedTile, ref selectedCrew, ref selectedMachine);
        ImGui.Separator();
        _stockpile.Render(sim, ref selectedTile);
        ImGui.Separator();
        _production.Render(sim, ref selectedMachine, ref selectedTile);

        if (!string.IsNullOrEmpty(_status))
        {
            ImGui.Separator();
            ImGui.TextColored(new float4(0.6f, 0.85f, 1.0f, 1.0f), _status);
        }

        ImGui.End();
    }

    private void RenderJobs(
        SimulationState sim,
        ref GridCoord? selectedTile,
        ref CrewMember? selectedCrew,
        ref Machine? selectedMachine)
    {
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f),
            $"JOBS ({sim.JobManager.ActiveJobs.Count})");
        var jobs = sim.JobManager.ActiveJobs.Take(8).ToList();
        if (jobs.Count == 0)
            ImGui.TextDisabled("No active jobs.");

        for (int index = 0; index < jobs.Count; index++)
        {
            Job job = jobs[index];
            string assignee = job.AssignedCrewId.HasValue
                ? sim.Crew.FirstOrDefault(agent => agent.Crew.Id == job.AssignedCrewId.Value)?.Crew.Name ?? "missing"
                : "unassigned";
            ImGui.BulletText($"{job.State} {job.Type} p{job.Priority} - {assignee}");
            if (job.Priority >= 100)
            {
                ImGui.SameLine();
                ImGui.TextColored(new float4(1.0f, 0.35f, 0.3f, 1.0f), "EMERGENCY");
            }

            string? reason = job.BlockedReason ?? job.LastFailureReason;
            if (!string.IsNullOrWhiteSpace(reason))
                ImGui.TextDisabled($"   {reason}");

            if (ImGui.Button($"Select #{index + 1}"))
            {
                selectedTile = job.TargetCoord;
                selectedCrew = job.AssignedCrewId.HasValue
                    ? sim.Crew.FirstOrDefault(agent => agent.Crew.Id == job.AssignedCrewId.Value)?.Crew
                    : null;
                selectedMachine = sim.Machines.FirstOrDefault(machine => machine.Position == job.TargetCoord);
            }
            ImGui.SameLine();
            if (ImGui.Button($"Cancel #{index + 1}"))
                Execute(sim, new CancelJobCommand(job.Id));
            ImGui.SameLine();
            if (ImGui.Button($"Reassign #{index + 1}"))
                Execute(sim, new ReassignJobCommand(job.Id));
            ImGui.SameLine();
            if (ImGui.Button($"+Pri #{index + 1}"))
                Execute(sim, new SetJobPriorityCommand(job.Id, job.Priority + 10));
            ImGui.SameLine();
            if (ImGui.Button($"-Pri #{index + 1}"))
                Execute(sim, new SetJobPriorityCommand(job.Id, job.Priority - 10));
        }
    }

    private void Execute(SimulationState sim, ISimulationCommand command)
    {
        var result = sim.ExecuteCommand(command);
        _status = result.Success
            ? result.Message
            : $"Rejected ({result.FailureCode}): {result.Message}";
    }
}
