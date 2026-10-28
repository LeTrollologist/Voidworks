using System;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Commands;

public sealed record SetMachineOnlineCommand(
    Guid MachineId,
    bool Online) : ISimulationCommand
{
    public string Name => "SetMachineOnline";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");
        if (Online && machine.Type == MachineType.Reactor && machine.IsScrammed)
            return SimulationCommandResult.Fail("ReactorScrammed",
                "The reactor is SCRAMed; reset the SCRAM before starting it again.");

        machine.IsOnline = Online;
        return SimulationCommandResult.Ok($"{machine.Type} at {machine.Position} turned {(Online ? "on" : "off")}.");
    }
}

public sealed record ToggleUtilityCommand(
    GridCoord Target,
    UtilityKind Utility) : ISimulationCommand
{
    public string Name => "ToggleUtility";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        if (!simulation.Grid.IsInBounds(Target))
            return SimulationCommandResult.Fail("InvalidCoordinate", $"Target {Target} is outside the ship grid.");

        TileType tile = simulation.Grid[Target].Type;
        if (tile is TileType.Space or TileType.Asteroid)
            return SimulationCommandResult.Fail("UtilityNotAllowed",
                $"Utilities cannot be installed on {tile} at {Target}.");

        switch (Utility)
        {
            case UtilityKind.PowerCable:
            {
                bool removed = simulation.Cables.Remove(Target);
                if (!removed) simulation.Cables.Add(Target);
                return SimulationCommandResult.Ok($"Power cable {(removed ? "removed" : "laid")} at {Target}.");
            }
            case UtilityKind.CoolantPipe:
            {
                bool removed = simulation.PlumbingSolver.CoolantPipes.Remove(Target);
                if (!removed) simulation.PlumbingSolver.CoolantPipes.Add(Target);
                return SimulationCommandResult.Ok($"Coolant pipe {(removed ? "removed" : "laid")} at {Target}.");
            }
            case UtilityKind.PropellantPipe:
            {
                bool removed = simulation.PlumbingSolver.PropellantPipes.Remove(Target);
                if (!removed) simulation.PlumbingSolver.PropellantPipes.Add(Target);
                return SimulationCommandResult.Ok($"Fuel line {(removed ? "removed" : "laid")} at {Target}.");
            }
            default:
                return SimulationCommandResult.Fail("InvalidUtility", $"Unknown utility kind {Utility}.");
        }
    }
}

public sealed record ResetReactorScramCommand(Guid MachineId) : ISimulationCommand
{
    public string Name => "ResetReactorScram";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Machine? machine = simulation.Machines.FirstOrDefault(candidate => candidate.Id == MachineId);
        if (machine == null)
            return SimulationCommandResult.Fail("MissingMachine", $"No machine with id {MachineId} exists.");
        if (machine.Type != MachineType.Reactor)
            return SimulationCommandResult.Fail("NotAReactor", $"{machine.Type} at {machine.Position} is not a reactor.");
        if (!machine.IsScrammed)
            return SimulationCommandResult.Fail("NotScrammed", "The reactor is not currently SCRAMed.");
        if (!machine.CanResetScram)
            return SimulationCommandResult.Fail("CoreTooHot",
                $"Core temperature {machine.CoreTemperatureKelvin:F0} K is above the {machine.ScramResetTemperatureKelvin:F0} K reset limit.");

        machine.TryResetScram();
        return SimulationCommandResult.Ok("Reactor SCRAM reset; reactor restarted.");
    }
}

public sealed record ToggleDoorCommand(GridCoord Target) : ISimulationCommand
{
    public string Name => "ToggleDoor";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        if (!simulation.Grid.IsInBounds(Target))
            return SimulationCommandResult.Fail("InvalidCoordinate", $"Target {Target} is outside the ship grid.");

        ShipTile tile = simulation.Grid[Target];
        if (tile.Type != TileType.Door)
            return SimulationCommandResult.Fail("NotADoor", $"Tile {Target} is {tile.Type}, not a door.");
        if (tile.DoorState == DoorState.Locked)
            return SimulationCommandResult.Fail("DoorLocked",
                $"Door at {Target} is locked; crew assistance or a valid access command is required.");

        tile.DoorState = tile.DoorState == DoorState.Open ? DoorState.Closed : DoorState.Open;
        return SimulationCommandResult.Ok($"Door at {Target} is now {tile.DoorState}.");
    }
}
