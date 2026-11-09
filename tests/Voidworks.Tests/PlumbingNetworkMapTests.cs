using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Plumbing;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class PlumbingNetworkMapTests
{
    [Fact]
    public void ConnectedCoolantLoop_ReportsOneFlowingSectionAndDirectionTowardRadiator()
    {
        SimulationState sim = CreateWorld();
        var reactor = AddMachine(sim, MachineType.Reactor, new GridCoord(3, 3));
        var radiator = AddMachine(sim, MachineType.Radiator, new GridCoord(8, 3));
        radiator.IsPowered = true;
        LayCoolantPipe(sim, 3, 3, 8);

        PlumbingNetworkMap map = PlumbingNetworkMap.Analyze(sim);

        PipeSection section = Assert.Single(map.CoolantSections);
        Assert.True(section.HasSource);
        Assert.True(section.HasSink);
        Assert.True(section.IsFlowing);
        Assert.Null(section.BlockedReason);
        Assert.Equal(6, section.Tiles.Count);
        Assert.Contains(reactor, section.Machines);
        Assert.Contains(radiator, section.Machines);
        Assert.Equal(0.5, section.FlowRate, 6);

        // Flow runs from the reactor toward the radiator. Tiles adjacent to the
        // radiator are the sink contact and therefore carry no arrow.
        Assert.Equal(new GridCoord(4, 3), map.CoolantFlowDirections[new GridCoord(3, 3)]);
        Assert.Equal(new GridCoord(7, 3), map.CoolantFlowDirections[new GridCoord(6, 3)]);
        Assert.DoesNotContain(new GridCoord(7, 3), map.CoolantFlowDirections.Keys);
        Assert.DoesNotContain(new GridCoord(8, 3), map.CoolantFlowDirections.Keys);
    }

    [Fact]
    public void SeveredCoolantLoop_ReportsTwoIncompleteSections()
    {
        SimulationState sim = CreateWorld();
        AddMachine(sim, MachineType.Reactor, new GridCoord(3, 3));
        var radiator = AddMachine(sim, MachineType.Radiator, new GridCoord(8, 3));
        radiator.IsPowered = true;
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(3, 3));
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(4, 3));
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(8, 3));

        PlumbingNetworkMap map = PlumbingNetworkMap.Analyze(sim);

        Assert.Equal(2, map.CoolantSections.Count);
        PipeSection reactorSide = map.CoolantSections.Single(section => section.HasSource);
        PipeSection radiatorSide = map.CoolantSections.Single(section => section.HasSink);
        Assert.True(reactorSide.IsStructurallyIncomplete);
        Assert.Equal("No radiator is connected to this coolant section.", reactorSide.BlockedReason);
        Assert.Equal("No reactor is connected to this coolant section.", radiatorSide.BlockedReason);
        Assert.False(reactorSide.IsFlowing);
        Assert.Empty(map.CoolantFlowDirections);
    }

    [Fact]
    public void DryCoolantLoop_ReportsDryReasonAndNoFlow()
    {
        SimulationState sim = CreateWorld();
        AddMachine(sim, MachineType.Reactor, new GridCoord(3, 3));
        var radiator = AddMachine(sim, MachineType.Radiator, new GridCoord(8, 3));
        radiator.IsPowered = true;
        LayCoolantPipe(sim, 3, 3, 8);
        sim.PlumbingSolver.CoolantStoredLiters = 0.0;

        PipeSection section = Assert.Single(PlumbingNetworkMap.Analyze(sim).CoolantSections);

        Assert.False(section.IsFlowing);
        Assert.Equal(0.0, section.FlowRate);
        Assert.Equal("Coolant loop is dry; refill from the reserve.", section.BlockedReason);
    }

    [Fact]
    public void UnpoweredCoolantPump_ReportsPowerReason()
    {
        SimulationState sim = CreateWorld();
        AddMachine(sim, MachineType.Reactor, new GridCoord(3, 3));
        var radiator = AddMachine(sim, MachineType.Radiator, new GridCoord(8, 3));
        radiator.IsPowered = false;
        LayCoolantPipe(sim, 3, 3, 8);

        PipeSection section = Assert.Single(PlumbingNetworkMap.Analyze(sim).CoolantSections);

        Assert.False(section.IsFlowing);
        Assert.Equal("Radiator coolant pump has no power.", section.BlockedReason);
    }

    [Fact]
    public void UnexposedRadiator_ReportsExposureReason()
    {
        SimulationState sim = CreateWorld();
        AddMachine(sim, MachineType.Reactor, new GridCoord(3, 3));
        var radiator = AddMachine(sim, MachineType.Radiator, new GridCoord(4, 3));
        radiator.IsPowered = true;
        LayCoolantPipe(sim, 3, 3, 4);

        PipeSection section = Assert.Single(PlumbingNetworkMap.Analyze(sim).CoolantSections);

        Assert.False(section.IsFlowing);
        Assert.Equal("Radiator is not exposed to space.", section.BlockedReason);
    }

    [Fact]
    public void PropellantLine_ReportsFlowDirectionTowardEngine()
    {
        SimulationState sim = CreateWorld();
        var tank = AddMachine(sim, MachineType.FuelTank, new GridCoord(2, 3));
        tank.FuelStoredKg = 20.0;
        var engine = AddMachine(sim, MachineType.MainEngine, new GridCoord(7, 3));
        engine.IsPowered = true;
        engine.IsFiring = true;
        LayPropellantPipe(sim, 2, 3, 7);
        sim.PlumbingSolver.Tick(sim.Grid, sim.Machines, SimulationState.FixedTickDt);

        PlumbingNetworkMap map = PlumbingNetworkMap.Analyze(sim);

        PipeSection section = Assert.Single(map.PropellantSections);
        Assert.True(section.IsFlowing);
        Assert.Null(section.BlockedReason);
        Assert.True(section.FlowRate > 0.0);
        Assert.Equal(new GridCoord(3, 3), map.PropellantFlowDirections[new GridCoord(2, 3)]);
        Assert.Equal(new GridCoord(6, 3), map.PropellantFlowDirections[new GridCoord(5, 3)]);
        Assert.DoesNotContain(new GridCoord(6, 3), map.PropellantFlowDirections.Keys);
    }

    [Fact]
    public void EmptyFuelTank_ReportsEmptyReason()
    {
        SimulationState sim = CreateWorld();
        var tank = AddMachine(sim, MachineType.FuelTank, new GridCoord(2, 3));
        tank.FuelStoredKg = 0.0;
        AddMachine(sim, MachineType.MainEngine, new GridCoord(7, 3));
        LayPropellantPipe(sim, 2, 3, 7);

        PipeSection section = Assert.Single(PlumbingNetworkMap.Analyze(sim).PropellantSections);

        Assert.False(section.IsFlowing);
        Assert.Equal("The connected fuel tanks are empty.", section.BlockedReason);
    }

    [Fact]
    public void SectionIds_AreStableAcrossAnalyses()
    {
        SimulationState sim = CreateWorld();
        var tank = AddMachine(sim, MachineType.FuelTank, new GridCoord(2, 3));
        tank.FuelStoredKg = 5.0;
        AddMachine(sim, MachineType.MainEngine, new GridCoord(7, 3));
        LayPropellantPipe(sim, 2, 3, 7);
        sim.PlumbingSolver.PropellantPipes.Add(new GridCoord(9, 5));

        PlumbingNetworkMap first = PlumbingNetworkMap.Analyze(sim);
        PlumbingNetworkMap second = PlumbingNetworkMap.Analyze(sim);

        Assert.Equal(first.PropellantSections.Select(section => section.Id),
            second.PropellantSections.Select(section => section.Id));
        Assert.Equal(first.PropellantSections.Select(section => section.Tiles.Count),
            second.PropellantSections.Select(section => section.Tiles.Count));
    }

    private static SimulationState CreateWorld()
    {
        var grid = new ShipGrid(10, 10);
        grid.BuildRoom(1, 1, 8, 8, TileType.HullWall);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());
        return sim;
    }

    private static Machine AddMachine(SimulationState sim, MachineType type, GridCoord position)
    {
        var machine = new Machine(type, position);
        sim.Machines.Add(machine);
        return machine;
    }

    private static void LayCoolantPipe(SimulationState sim, int fromX, int y, int toX)
    {
        for (int x = fromX; x <= toX; x++)
            sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(x, y));
    }

    private static void LayPropellantPipe(SimulationState sim, int fromX, int y, int toX)
    {
        for (int x = fromX; x <= toX; x++)
            sim.PlumbingSolver.PropellantPipes.Add(new GridCoord(x, y));
    }
}
