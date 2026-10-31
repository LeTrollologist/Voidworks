using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Power;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class PowerAndPlumbingDiagnosticsTests
{
    [Fact]
    public void PowerDiagnostics_DetectsDisconnectedMachine_ThenRecovery()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2))
        {
            BatteryStoredKWh = 100.0,
            BatteryCapacityKWh = 100.0
        };
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(5, 5));
        sim.Machines.Add(battery);
        sim.Machines.Add(smelter);

        sim.Step(SimulationState.FixedTickDt);
        Assert.Contains("No power source", PowerDiagnostics.ExplainMachinePower(sim, smelter));

        foreach (var coord in new[]
                 {
                     new GridCoord(2, 2), new GridCoord(3, 2), new GridCoord(4, 2),
                     new GridCoord(5, 2), new GridCoord(5, 3), new GridCoord(5, 4), new GridCoord(5, 5)
                 })
        {
            sim.Cables.Add(coord);
        }

        for (int tick = 0; tick < 3; tick++)
            sim.Step(SimulationState.FixedTickDt);

        Assert.True(smelter.IsPowered);
        Assert.Null(PowerDiagnostics.ExplainMachinePower(sim, smelter));
    }

    [Fact]
    public void PowerDiagnostics_ReportsDepletedBattery()
    {
        var grid = new ShipGrid(8, 8);
        grid.BuildRoom(1, 1, 6, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var battery = new Machine(MachineType.Battery, new GridCoord(2, 2)) { BatteryStoredKWh = 0.0 };
        var smelter = new Machine(MachineType.SmelterRefinery, new GridCoord(3, 2));
        sim.Machines.Add(battery);
        sim.Machines.Add(smelter);
        sim.Cables.Add(new GridCoord(2, 2));
        sim.Cables.Add(new GridCoord(3, 2));

        sim.Step(SimulationState.FixedTickDt);

        Assert.Contains("depleted", PowerDiagnostics.ExplainMachinePower(sim, smelter));
    }

    [Fact]
    public void CoolantDiagnostics_ReportsSeveredLoop_AndClearsAfterRepair()
    {
        var grid = new ShipGrid(10, 8);
        grid.BuildRoom(1, 1, 8, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        sim.Machines.Add(new Machine(MachineType.Reactor, new GridCoord(2, 2)));
        sim.Machines.Add(new Machine(MachineType.Radiator, new GridCoord(7, 2)));
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(2, 2));
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(7, 2));

        Assert.Contains("severed", PlumbingDiagnostics.ExplainCoolant(sim));

        for (int x = 2; x <= 7; x++)
            sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(x, 2));

        string? repaired = PlumbingDiagnostics.ExplainCoolant(sim);
        Assert.True(repaired == null || !repaired.Contains("severed"));
    }

    [Fact]
    public void PropellantDiagnostics_ReportsMissingTankAndPipes()
    {
        var grid = new ShipGrid(10, 8);
        grid.BuildRoom(1, 1, 8, 6, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        var engine = new Machine(MachineType.MainEngine, new GridCoord(6, 4)) { IsFiring = true };
        sim.Machines.Add(engine);

        Assert.Contains("No propellant remains", PlumbingDiagnostics.ExplainPropellant(sim));

        var tank = new Machine(MachineType.FuelTank, new GridCoord(2, 4)) { FuelStoredKg = 100.0 };
        sim.Machines.Add(tank);
        Assert.Contains("No propellant lines", PlumbingDiagnostics.ExplainPropellant(sim));

        for (int x = 2; x <= 6; x++)
            sim.PlumbingSolver.PropellantPipes.Add(new GridCoord(x, 4));
        string? repaired = PlumbingDiagnostics.ExplainPropellant(sim);
        Assert.True(repaired == null || !repaired.Contains("No propellant lines"));
    }
}
