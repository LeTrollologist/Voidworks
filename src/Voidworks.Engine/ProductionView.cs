using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class ProductionView
{
    public void Render(SimulationState sim, ref Machine? selectedMachine, ref GridCoord? selectedTile)
    {
        var power = sim.PowerGridSolver;
        ImGui.TextDisabled(
            $"Power {power.TotalGenerationKW:F0}/{power.TotalDemandKW:F0} kW | " +
            $"unserved {power.TotalUnservedDemandKW:F0} kW | brownouts {power.BrownoutMachineCount}");

        var plumbing = sim.PlumbingSolver;
        ImGui.TextDisabled(
            $"Coolant {plumbing.TotalCoolantFlowRateLitersPerSecond:F2} L/s | " +
            $"leak {plumbing.TotalCoolantLeakRateLitersPerSecond:F2} L/s | " +
            $"{plumbing.CoolantStoredLiters:F0} L | propellant {plumbing.TotalPropellantFlowRateKgPerSec:F2} kg/s");

        string? coolantIssue = PlumbingDiagnostics.ExplainCoolant(sim);
        if (!string.IsNullOrWhiteSpace(coolantIssue))
            ImGui.TextColored(new float4(1.0f, 0.55f, 0.25f, 1.0f), $"Coolant: {coolantIssue}");

        string? propellantIssue = PlumbingDiagnostics.ExplainPropellant(sim);
        if (!string.IsNullOrWhiteSpace(propellantIssue))
            ImGui.TextColored(new float4(1.0f, 0.55f, 0.25f, 1.0f), $"Propellant: {propellantIssue}");

        ImGui.TextColored(new float4(0.4f, 1.0f, 0.8f, 1.0f), $"PRODUCTION ({sim.Machines.Count})");
        if (sim.Machines.Count == 0)
            ImGui.TextDisabled("No machinery installed.");

        foreach (var machine in sim.Machines.Take(8))
        {
            if (ImGui.Button($"{machine.Type} @ {machine.Position}"))
            {
                selectedMachine = machine;
                selectedTile = machine.Position;
            }

            ImGui.SameLine();
            var (label, color) = machine switch
            {
                { IsOnline: false } => ("OFFLINE", new float4(1.0f, 0.35f, 0.3f, 1.0f)),
                { PowerFraction: <= 0.0 } => ("NO POWER", new float4(1.0f, 0.35f, 0.3f, 1.0f)),
                { PowerFraction: < 1.0 } => ($"BROWNOUT {machine.PowerFraction:P0}", new float4(1.0f, 0.7f, 0.3f, 1.0f)),
                _ => ("ONLINE", new float4(0.4f, 1.0f, 0.5f, 1.0f))
            };
            ImGui.TextColored(color, label);
            string control = machine.ProductionPaused
                ? "PAUSED"
                : machine.ProductionLimit >= 0
                    ? $"limit {machine.ProductionCyclesCompleted}/{machine.ProductionLimit}"
                    : $"cycles {machine.ProductionCyclesCompleted}";
            ImGui.TextDisabled($"   {Describe(machine)} | {control} | condition {machine.Condition:F0}%");
            if (machine.ProductionQueue.Count > 0)
            {
                string queued = string.Join(", ", machine.ProductionQueue
                    .Take(4)
                    .Select(order => $"{order.Recipe} x{order.Quantity}"));
                ImGui.TextDisabled($"   Queue: {queued} ({machine.QueuedCycles} cycles)");
            }

            string? blockReason = ProductionDiagnostics.GetBlockReason(sim, machine);
            if (!string.IsNullOrWhiteSpace(blockReason))
                ImGui.TextColored(new float4(1.0f, 0.55f, 0.25f, 1.0f), $"   {blockReason}");
        }
    }

    private static string Describe(Machine machine) => machine.Type switch
    {
        MachineType.MiningLaser =>
            $"extracted {machine.TotalOreExtracted} | {machine.MiningProgressSeconds:F1}/{machine.MiningCycleSeconds:F1}s",
        MachineType.SmelterRefinery =>
            $"ore {machine.IronOreStored} | {machine.SmeltProgressSeconds:F1}/{machine.SmeltCycleSeconds:F1}s | plates {machine.SteelPlatesProduced}",
        MachineType.ComponentFabricator =>
            $"{machine.ComponentFabricationProgressSeconds:F1}/{machine.ComponentFabricationCycleSeconds:F1}s | components {machine.ComponentsProduced}",
        MachineType.IceProcessor =>
            $"{(machine.ProduceFuelCanisters ? "propellant" : "water")} | " +
            $"{machine.IceProcessingProgressSeconds:F1}/{machine.IceProcessingCycleSeconds:F1}s | " +
            $"water {machine.WaterRationsProduced} fuel {machine.FuelCanistersProduced}",
        MachineType.FuelTank =>
            $"fuel {machine.FuelStoredKg:F0}/{machine.FuelCapacityKg:F0} kg",
        MachineType.HydroponicsBay =>
            $"water {machine.WaterStoredLiters:F1} L | food {machine.FoodRationsProduced}",
        MachineType.Battery =>
            $"{machine.BatteryStoredKWh:F0}/{machine.BatteryCapacityKWh:F0} kWh",
        MachineType.Reactor =>
            $"{machine.CoreTemperatureKelvin:F0} K{(machine.IsScrammed ? " | SCRAM" : string.Empty)}",
        _ => $"power {machine.PowerFraction:P0}"
    };
}
