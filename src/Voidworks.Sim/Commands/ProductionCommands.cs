using System;
using System.Linq;
using Voidworks.Sim.Machinery;

namespace Voidworks.Sim.Commands;

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
