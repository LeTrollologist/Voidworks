using System;
using System.Linq;
using Voidworks.Sim;
using Voidworks.Sim.Atmosphere;
using Voidworks.Sim.Commands;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Diagnostics;
using Voidworks.Sim.Jobs;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Ship;
using Xunit;

namespace Voidworks.Tests;

public class WorldTopologyAndAlertTests
{
    [Fact]
    public void CableOrPipe_OnSpace_IsReported()
    {
        SimulationState sim = CommandTestWorld.Create();
        sim.Cables.Add(new GridCoord(0, 0));
        sim.PlumbingSolver.CoolantPipes.Add(new GridCoord(0, 1));
        sim.PlumbingSolver.PropellantPipes.Add(new GridCoord(0, 2));

        var violations = sim.ValidateInvariants();

        Assert.Equal(3, violations.Count(violation => violation.Code == "UtilityOnInvalidTile"));
    }

    [Fact]
    public void Door_WithoutWalkableNeighbor_IsReported()
    {
        var grid = new ShipGrid(6, 6);
        grid.PlaceDoor(3, 3, DoorState.Closed);
        var sim = new SimulationState(grid, new AtmosphereSolver(), new JobManager());

        var violations = sim.ValidateInvariants();

        Assert.Contains(violations, violation => violation.Code == "DoorWithoutWalkableNeighbor");
    }

    [Fact]
    public void IsolatedUtility_IsReported_ButMachineAttachedIsNot()
    {
        SimulationState sim = CommandTestWorld.Create();
        var isolated = new GridCoord(4, 4);
        sim.Cables.Add(isolated);

        Assert.Contains(sim.ValidateInvariants(),
            violation => violation.Code == "UtilityWithoutConnection");

        var machineTile = new GridCoord(5, 4);
        sim.Machines.Add(new Machine(MachineType.Battery, machineTile));
        sim.Cables.Add(machineTile);

        Assert.DoesNotContain(sim.ValidateInvariants(), violation =>
            violation.Code == "UtilityWithoutConnection" && violation.Message.Contains(machineTile.ToString()));
    }

    [Fact]
    public void WalkableTile_OpenToSpace_IsReported()
    {
        SimulationState sim = CommandTestWorld.Create();
        sim.Grid[8, 3].Type = TileType.Space;

        Assert.Contains(sim.ValidateInvariants(),
            violation => violation.Code == "RoomOpensToSpace");
    }

    [Fact]
    public void Crew_OnStructuralTile_IsReported()
    {
        SimulationState sim = CommandTestWorld.Create();
        sim.Crew.Add(new CrewAI(new CrewMember("Test Walker", new GridCoord(1, 1))));

        var violations = sim.ValidateInvariants();

        Assert.Contains(violations, violation => violation.Code == "CrewOnInvalidTile");
    }

    [Fact]
    public void AlertFeed_MapsSeveritiesAndIgnoresRoutineEvents()
    {
        var log = new SimulationEventLog();
        log.Add(1.0, SimulationEventKind.JobFailed, "Refine job failed after 3 attempts");
        log.Add(2.0, SimulationEventKind.CrewDied, "Engineer Miller died at (4, 3)");
        log.Add(3.0, SimulationEventKind.JobPosted, "Routine job posted");
        log.Add(4.0, SimulationEventKind.CommandRejected, "BuildTile rejected (TileOccupied): occupied");
        log.Add(5.0, SimulationEventKind.JobBlocked, "Repair job blocked: no torch");

        var alerts = SimulationAlertFeed.Build(log.Recent);

        Assert.Equal(4, alerts.Count);
        Assert.Contains(alerts, alert => alert.Title == "Job failed" && alert.Severity == AlertSeverity.Critical);
        Assert.Contains(alerts, alert => alert.Title == "Crew member died" && alert.Severity == AlertSeverity.Critical);
        Assert.Contains(alerts, alert => alert.Title == "Command rejected" && alert.Severity == AlertSeverity.Warning);
        Assert.Contains(alerts, alert => alert.Title == "Job blocked" && alert.Severity == AlertSeverity.Warning);
        Assert.DoesNotContain(alerts, alert => alert.Message.Contains("Routine"));
    }

    [Fact]
    public void AlertFeed_IsBoundedToNewestEntries()
    {
        var log = new SimulationEventLog();
        for (int i = 0; i < 5; i++)
            log.Add(i, SimulationEventKind.JobFailed, $"failure {i}");

        var alerts = SimulationAlertFeed.Build(log.Recent, capacity: 2);

        Assert.Equal(2, alerts.Count);
        Assert.Equal("failure 4", alerts[^1].Message);
        Assert.Equal("failure 3", alerts[0].Message);
    }

    [Fact]
    public void SimulationStateExposesCommandRejectionsAsAlerts()
    {
        SimulationState sim = CommandTestWorld.Create();

        sim.ExecuteCommand(new BuildTileCommand(new GridCoord(99, 99), TileType.Floor, 1, 3.0));

        Assert.Contains(sim.GetAlerts(), alert =>
            alert.Title == "Command rejected" && alert.Message.Contains("InvalidCoordinate"));
    }
}
