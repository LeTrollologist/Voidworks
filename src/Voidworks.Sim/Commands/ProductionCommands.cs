using System;
using System.Linq;
using Voidworks.Sim.Machinery;

namespace Voidworks.Sim.Commands;

public sealed record QueueProductionCommand(
    Guid MachineId,
    ProductionRecipe Recipe,
    int Cycles) : ISimulationCommand
{
    public string Name => "QueueProduction";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");
        if (!machine.SupportsRecipe(Recipe))
            return SimulationCommandResult.Fail("UnsupportedRecipe",
                $"{machine.Type} at {machine.Position} cannot run {Recipe}.");
        if (Cycles <= 0)
            return SimulationCommandResult.Fail("InvalidQuantity", "Production quantity must be at least one cycle.");
        if (!machine.EnqueueProduction(Recipe, Cycles))
            return SimulationCommandResult.Fail("QueueFull",
                $"Production queue for {machine.Type} at {machine.Position} is full ({machine.QueuedCycles}/{machine.MaxQueuedCycles} cycles).");

        return SimulationCommandResult.Ok(
            $"Queued {Cycles} x {Recipe} on {machine.Type} at {machine.Position}.");
    }
}

public sealed record ClearProductionQueueCommand(Guid MachineId) : ISimulationCommand
{
    public string Name => "ClearProductionQueue";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");

        int cleared = machine.QueuedCycles;
        machine.ClearProductionQueue();
        return SimulationCommandResult.Ok($"Cleared {cleared} queued cycles on {machine.Type} at {machine.Position}.");
    }
}

public sealed record SetAutoProductionCommand(
    Guid MachineId,
    bool Enabled) : ISimulationCommand
{
    public string Name => "SetAutoProduction";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");

        machine.AutoProductionEnabled = Enabled;
        return SimulationCommandResult.Ok(
            $"Automatic production for {machine.Type} at {machine.Position} {(Enabled ? "enabled" : "disabled")}.");
    }
}

public sealed record SetProductionPausedCommand(
    Guid MachineId,
    bool Paused) : ISimulationCommand
{
    public string Name => "SetProductionPaused";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");

        machine.ProductionPaused = Paused;
        return SimulationCommandResult.Ok(
            $"{machine.Type} at {machine.Position} production {(Paused ? "paused" : "resumed")}.");
    }
}

public sealed record SetProductionLimitCommand(
    Guid MachineId,
    int Limit) : ISimulationCommand
{
    public string Name => "SetProductionLimit";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");
        if (Limit < -1)
            return SimulationCommandResult.Fail("InvalidLimit", "Production limit must be -1 (unlimited) or a non-negative cycle count.");

        machine.ProductionLimit = Limit;
        string description = Limit < 0 ? "unlimited" : $"{Limit} cycles";
        return SimulationCommandResult.Ok($"{machine.Type} at {machine.Position} production limit set to {description}.");
    }
}
