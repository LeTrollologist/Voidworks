using System;
using System.Linq;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;

namespace Voidworks.Sim.Commands;

public sealed record RepairMachineCommand(Guid MachineId) : ISimulationCommand
{
    public string Name => "RepairMachine";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");
        if (machine.Condition >= 100.0)
            return SimulationCommandResult.Fail("NotDamaged", $"{machine.Type} at {machine.Position} is already at full condition.");
        if (simulation.JobManager.ActiveJobs.Any(job => job.Type == JobType.RepairMachine &&
                                                         job.TargetCoord == machine.Position && !job.IsTerminal))
            return SimulationCommandResult.Fail("PendingOrder",
                $"A repair order is already queued for {machine.Type} at {machine.Position}.");

        double workSeconds = 2.0 + (100.0 - machine.Condition) / 20.0;
        Job job = Job.CreateRepairMachine(machine.Position, workSeconds);
        simulation.JobManager.PostJob(job);
        return SimulationCommandResult.Ok($"Queued repair of {machine.Type} at {machine.Position}.", job.Id);
    }
}

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
