using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class CommandLayerTests
{
    [Fact]
    public void BuildTile_OnSpaceAdjacentToFloor_QueuesJob()
    {
        SimulationState sim = CommandTestWorld.Create();
        GridCoord target = CommandTestWorld.OpenHullTile(sim);

        var result = sim.ExecuteCommand(new BuildTileCommand(target, TileType.Floor, 1, 3.0));

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.JobId);
        var job = Assert.Single(sim.JobManager.ActiveJobs);
        Assert.Equal(JobType.Construct, job.Type);
        Assert.Equal(target, job.TargetCoord);
    }

    [Fact]
    public void BuildTile_OnOccupiedTile_IsRejectedWithReason()
    {
        SimulationState sim = CommandTestWorld.Create();
        GridCoord target = CommandTestWorld.OpenHullTile(sim);
        sim.Machines.Add(new Machine(MachineType.Battery, target));

        var result = sim.ExecuteCommand(new BuildTileCommand(target, TileType.Floor, 1, 3.0));

        Assert.False(result.Success);
        Assert.Equal("TileOccupied", result.FailureCode);
        Assert.Empty(sim.JobManager.ActiveJobs);
    }

    [Fact]
    public void InstallMachine_RequiresFloorAndEmptyTile()
    {
        SimulationState sim = CommandTestWorld.Create();
        var success = sim.ExecuteCommand(
            new InstallMachineCommand(new GridCoord(4, 4), MachineType.SmelterRefinery, 6, 0, 12.0));
        Assert.True(success.Success, success.Message);

        var wall = sim.ExecuteCommand(
            new InstallMachineCommand(new GridCoord(1, 4), MachineType.Battery, 4, 0, 8.0));
        Assert.False(wall.Success);
        Assert.Equal("InvalidTile", wall.FailureCode);
    }

    [Fact]
    public void Deconstruct_WithMachine_RequiresDismantleFirst()
    {
        SimulationState sim = CommandTestWorld.Create();
        sim.Machines.Add(new Machine(MachineType.Battery, new GridCoord(3, 3)));

        var blocked = sim.ExecuteCommand(new DeconstructTileCommand(new GridCoord(3, 3), 3.0));
        Assert.False(blocked.Success);
        Assert.Equal("MachinePresent", blocked.FailureCode);

        var missing = sim.ExecuteCommand(new DismantleMachineCommand(new GridCoord(4, 4), 6.0));
        Assert.False(missing.Success);
        Assert.Equal("MissingMachine", missing.FailureCode);

        var dismantle = sim.ExecuteCommand(new DismantleMachineCommand(new GridCoord(3, 3), 6.0));
        Assert.True(dismantle.Success, dismantle.Message);
    }

    [Fact]
    public void InstallMachine_AtSealedSite_IsRejectedAsUnreachable()
    {
        SimulationState sim = CommandTestWorld.Create();
        for (int x = 5; x <= 7; x++)
        {
            sim.Grid[x, 4].Type = TileType.InteriorWall;
            sim.Grid[x, 6].Type = TileType.InteriorWall;
        }
        sim.Grid[5, 5].Type = TileType.InteriorWall;
        sim.Grid[7, 5].Type = TileType.InteriorWall;

        var result = sim.ExecuteCommand(new InstallMachineCommand(
            new GridCoord(6, 5), MachineType.Battery, 4, 0, 3.0));

        Assert.False(result.Success);
        Assert.Equal("Unreachable", result.FailureCode);
    }

    [Fact]
    public void ToggleUtility_RejectsSpace_AndTogglesOnStructure()
    {
        SimulationState sim = CommandTestWorld.Create();
        var rejected = sim.ExecuteCommand(
            new ToggleUtilityCommand(new GridCoord(0, 0), UtilityKind.PowerCable));
        Assert.False(rejected.Success);
        Assert.Equal("UtilityNotAllowed", rejected.FailureCode);

        var coord = new GridCoord(4, 4);
        Assert.True(sim.ExecuteCommand(new ToggleUtilityCommand(coord, UtilityKind.PowerCable)).Success);
        Assert.Contains(coord, sim.Cables);
        Assert.True(sim.ExecuteCommand(new ToggleUtilityCommand(coord, UtilityKind.PowerCable)).Success);
        Assert.DoesNotContain(coord, sim.Cables);
    }

    [Fact]
    public void ToggleDoor_RejectsLocked_AndAlternatesOpenClosed()
    {
        SimulationState sim = CommandTestWorld.Create();
        sim.Grid.PlaceDoor(8, 3, DoorState.Locked);
        var locked = sim.ExecuteCommand(new ToggleDoorCommand(new GridCoord(8, 3)));
        Assert.False(locked.Success);
        Assert.Equal("DoorLocked", locked.FailureCode);

        sim.Grid.PlaceDoor(8, 4, DoorState.Closed);
        var opened = sim.ExecuteCommand(new ToggleDoorCommand(new GridCoord(8, 4)));
        Assert.True(opened.Success, opened.Message);
        Assert.Equal(DoorState.Open, sim.Grid[8, 4].DoorState);

        var notDoor = sim.ExecuteCommand(new ToggleDoorCommand(new GridCoord(4, 4)));
        Assert.False(notDoor.Success);
        Assert.Equal("NotADoor", notDoor.FailureCode);
    }
}
