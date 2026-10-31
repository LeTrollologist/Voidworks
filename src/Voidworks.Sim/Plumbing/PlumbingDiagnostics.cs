using System;
using System.Linq;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;

namespace Voidworks.Sim.Plumbing;

/// <summary>Explains coolant and propellant problems in player-facing terms.</summary>
public static class PlumbingDiagnostics
{
    public static string? ExplainCoolant(SimulationState sim)
    {
        var reactors = sim.Machines.Where(machine => machine.Type == MachineType.Reactor).ToList();
        var radiators = sim.Machines.Where(machine => machine.Type == MachineType.Radiator).ToList();

        if (reactors.Count == 0)
            return "No reactor is installed.";
        if (radiators.Count == 0)
            return "No radiator is installed.";
        if (sim.PlumbingSolver.CoolantPipes.Count == 0)
            return "No coolant pipes are installed.";

        bool connected = reactors.Any(reactor => radiators.Any(radiator =>
            sim.PlumbingSolver.IsCoolantLoopConnected(reactor.Position, radiator.Position)));
        if (!connected)
            return "Coolant loop is severed; reactor heat cannot reach the radiators.";

        if (sim.PlumbingSolver.CoolantStoredLiters <= 0.0)
            return "Coolant loop is dry; refill from the reserve.";

        bool radiatorPowered = radiators.Any(radiator => radiator.IsOnline && radiator.IsPowered);
        if (!radiatorPowered)
            return "Radiator coolant pumps have no power.";

        bool radiatorExposed = radiators.Any(radiator =>
            sim.Grid[radiator.Position].Type == TileType.Space ||
            radiator.Position.Cardinals()
                .Any(neighbor => sim.Grid.IsInBounds(neighbor) && sim.Grid[neighbor].Type == TileType.Space));
        if (!radiatorExposed)
            return "Radiators are not exposed to space; heat cannot be rejected.";

        if (sim.PlumbingSolver.TotalCoolantFlowRateLitersPerSecond <= 0.0)
            return "No coolant flow is reaching the reactor.";

        if (sim.PlumbingSolver.TotalCoolantLeakRateLitersPerSecond > 0.0)
            return $"Coolant leak: {sim.PlumbingSolver.TotalCoolantLeakRateLitersPerSecond:F2} L/s.";

        return null;
    }

    public static string? ExplainPropellant(SimulationState sim)
    {
        var engines = sim.Machines.Where(machine => machine.Type == MachineType.MainEngine).ToList();
        if (engines.Count == 0)
            return null;

        var tanks = sim.Machines.Where(machine => machine.Type == MachineType.FuelTank && machine.FuelStoredKg > 0.0).ToList();
        if (tanks.Count == 0)
            return "No propellant remains in the fuel tanks.";
        if (sim.PlumbingSolver.PropellantPipes.Count == 0)
            return "No propellant lines connect the tanks to the engine.";

        bool anyFed = engines.Any(engine => tanks.Any(tank =>
            sim.PlumbingSolver.PropellantPipes.Contains(engine.Position) ||
            engine.Position.Cardinals().Any(sim.PlumbingSolver.PropellantPipes.Contains) ||
            sim.PlumbingSolver.PropellantPipes.Contains(tank.Position) ||
            tank.Position.Cardinals().Any(sim.PlumbingSolver.PropellantPipes.Contains)));
        if (!anyFed)
            return "Propellant lines do not connect a tank to the engine.";

        var firingEngine = engines.FirstOrDefault(engine => engine.IsFiring);
        if (firingEngine == null)
            return "Engine is not firing.";
        if (sim.PlumbingSolver.TotalPropellantFlowRateKgPerSec <= 0.0)
            return "Engine flameout: no propellant flow.";

        return null;
    }
}
