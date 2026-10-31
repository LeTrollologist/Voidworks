using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

/// <summary>
/// Read-only diagnostic windows. These never mutate simulation state; they expose
/// the same job, reservation, route, utility, machine, and alert data that the
/// headless tests assert on.
/// </summary>
public sealed class SimulationDebugPanel
{
    public void RenderDebug(SimulationState sim)
    {
        ImGui.SetNextWindowPos(new float2(900f, 560f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(360f, 330f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Simulation Debug"))
        {
            ImGui.End();
            return;
        }

        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), $"JOBS ({sim.JobManager.ActiveJobs.Count})");
        foreach (var job in sim.JobManager.ActiveJobs.Take(8))
        {
            string assignee = job.AssignedCrewId.HasValue
                ? sim.Crew.FirstOrDefault(agent => agent.Crew.Id == job.AssignedCrewId.Value)?.Crew.Name ?? "missing"
                : "unassigned";
            ImGui.BulletText($"{job.State} {job.Type} p{job.Priority} - {assignee}");
            string? reason = job.BlockedReason ?? job.LastFailureReason;
            if (!string.IsNullOrWhiteSpace(reason))
                ImGui.TextDisabled($"   {reason}");
        }

        ImGui.Separator();
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), "RESERVATIONS");
        var reserved = sim.JobManager.WorldItems.Where(item => item.ReservedByCrewId.HasValue).Take(6).ToList();
        if (reserved.Count == 0)
            ImGui.TextDisabled("No loose items are reserved.");
        foreach (var item in reserved)
        {
            string owner = sim.Crew.FirstOrDefault(agent => agent.Crew.Id == item.ReservedByCrewId)?.Crew.Name ?? "missing";
            ImGui.BulletText($"{item.Name} -> {owner}");
        }
        ImGui.TextDisabled($"Crew inventories: {sim.Crew.Sum(agent => agent.Crew.Inventory.Count)} items");

        ImGui.Separator();
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), "ROUTES");
        foreach (var agent in sim.Crew)
        {
            ImGui.BulletText($"{agent.Crew.Name}: {agent.CurrentPath.Count} tiles, {agent.CurrentActivity}");
        }

        ImGui.Separator();
        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), "UTILITIES / MACHINES");
        ImGui.TextDisabled($"Cables {sim.Cables.Count} | Coolant {sim.PlumbingSolver.CoolantPipes.Count} | " +
                           $"Fuel {sim.PlumbingSolver.PropellantPipes.Count}");
        ImGui.TextDisabled($"Coolant flow {sim.PlumbingSolver.TotalCoolantFlowRateLitersPerSecond:F2} L/s | " +
                           $"Propellant {sim.PlumbingSolver.TotalPropellantFlowRateKgPerSec:F2} kg/s");
        foreach (var machine in sim.Machines.Take(6))
        {
            string state = !machine.IsOnline ? "OFF" : machine.PowerFraction <= 0.0 ? "UNPOWERED" : $"{machine.PowerFraction:P0}";
            string scram = machine.IsScrammed ? " SCRAM" : string.Empty;
            ImGui.TextDisabled($"{machine.Type} @ {machine.Position}: {state}{scram}");
        }

        ImGui.End();
    }

    public void RenderAlerts(
        SimulationState sim,
        ref GridCoord? selectedTile,
        ref CrewMember? selectedCrew,
        ref Machine? selectedMachine)
    {
        ImGui.SetNextWindowPos(new float2(490f, 560f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(400f, 330f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Alerts"))
        {
            ImGui.End();
            return;
        }

        var alerts = sim.GetAlerts();
        int critical = alerts.Count(alert => alert.Severity == AlertSeverity.Critical);
        int warning = alerts.Count(alert => alert.Severity == AlertSeverity.Warning);
        ImGui.TextColored(critical > 0 ? new float4(1.0f, 0.3f, 0.25f, 1.0f) : new float4(1.0f, 0.8f, 0.3f, 1.0f),
            $"ALERTS: {critical} critical, {warning} warning");
        ImGui.Separator();

        if (alerts.Count == 0)
        {
            ImGui.TextDisabled("No active alerts.");
            ImGui.End();
            return;
        }

        foreach (var alert in alerts.Reverse().Take(10))
        {
            var color = alert.Severity switch
            {
                AlertSeverity.Critical => new float4(1.0f, 0.35f, 0.3f, 1.0f),
                AlertSeverity.Warning => new float4(1.0f, 0.75f, 0.3f, 1.0f),
                _ => new float4(0.6f, 0.8f, 1.0f, 1.0f)
            };
            ImGui.TextColored(color, $"[{alert.SimTime:F0}s] {alert.Title}");
            ImGui.SameLine();
            if (ImGui.Button($"Locate {alert.Sequence}"))
            {
                if (alert.JobId is Guid jobId)
                {
                    var job = sim.JobManager.ActiveJobs
                        .Concat(sim.JobManager.FailedJobsHistory)
                        .FirstOrDefault(candidate => candidate.Id == jobId);
                    if (job != null)
                    {
                        GridCoord? focusTile = job.TargetItem?.Position ?? job.TargetCoord;
                        selectedTile = focusTile;
                        selectedCrew = job.AssignedCrewId.HasValue
                            ? sim.Crew.FirstOrDefault(agent => agent.Crew.Id == job.AssignedCrewId.Value)?.Crew
                            : null;
                        selectedMachine = focusTile.HasValue
                            ? sim.Machines.FirstOrDefault(machine => machine.Position == focusTile.Value)
                            : null;
                    }
                }
                else if (alert.CrewId is Guid crewId)
                {
                    var agent = sim.Crew.FirstOrDefault(candidate => candidate.Crew.Id == crewId);
                    if (agent != null)
                    {
                        selectedCrew = agent.Crew;
                        selectedTile = agent.Crew.Position;
                        selectedMachine = null;
                    }
                }
            }
            ImGui.TextDisabled($"   {alert.Message}");
        }

        ImGui.End();
    }
}
