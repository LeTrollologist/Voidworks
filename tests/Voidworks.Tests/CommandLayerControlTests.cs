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

public class CommandLayerControlTests
{
    [Fact]
    public void ReactorCommands_RespectScramAndResetLimits()
    {
        SimulationState sim = CommandTestWorld.Create();
        var reactor = new Machine(MachineType.Reactor, new GridCoord(5, 5))
        {
            IsScrammed = true,
            IsOnline = false,
            CoreTemperatureKelvin = 400.0
        };
        sim.Machines.Add(reactor);

        var start = sim.ExecuteCommand(new SetMachineOnlineCommand(reactor.Id, true));
        Assert.False(start.Success);
        Assert.Equal("ReactorScrammed", start.FailureCode);

        var reset = sim.ExecuteCommand(new ResetReactorScramCommand(reactor.Id));
        Assert.True(reset.Success, reset.Message);
        Assert.False(reactor.IsScrammed);

        var online = sim.ExecuteCommand(new SetMachineOnlineCommand(reactor.Id, true));
        Assert.True(online.Success, online.Message);
        Assert.True(reactor.IsOnline);
    }

    [Fact]
    public void ReactorReset_RejectedWhileCoreTooHot()
    {
        SimulationState sim = CommandTestWorld.Create();
        var reactor = new Machine(MachineType.Reactor, new GridCoord(5, 5))
        {
            IsScrammed = true,
            IsOnline = false,
            CoreTemperatureKelvin = 1000.0
        };
        sim.Machines.Add(reactor);

        var reset = sim.ExecuteCommand(new ResetReactorScramCommand(reactor.Id));

        Assert.False(reset.Success);
        Assert.Equal("CoreTooHot", reset.FailureCode);
        Assert.True(reactor.IsScrammed);
    }

    [Fact]
    public void JobCommands_UpdatePriorityAndCancel()
    {
        SimulationState sim = CommandTestWorld.Create();
        GridCoord target = CommandTestWorld.OpenHullTile(sim);
        var build = sim.ExecuteCommand(new BuildTileCommand(target, TileType.Floor, 1, 3.0));
        Guid jobId = build.JobId!.Value;

        var priority = sim.ExecuteCommand(new SetJobPriorityCommand(jobId, 75));
        Assert.True(priority.Success, priority.Message);
        Assert.Equal(75, sim.JobManager.ActiveJobs.Single(job => job.Id == jobId).Priority);

        var cancel = sim.ExecuteCommand(new CancelJobCommand(jobId));
        Assert.True(cancel.Success, cancel.Message);
        Assert.Empty(sim.JobManager.ActiveJobs);

        var missing = sim.ExecuteCommand(new CancelJobCommand(jobId));
        Assert.False(missing.Success);
        Assert.Equal("JobNotFound", missing.FailureCode);
    }

    [Fact]
    public void ReassignJob_ReleasesAssigneeAndReturnsToQueue()
    {
        SimulationState sim = CommandTestWorld.Create();
        GridCoord target = CommandTestWorld.OpenHullTile(sim);
        var build = sim.ExecuteCommand(new BuildTileCommand(target, TileType.Floor, 1, 3.0));
        Guid jobId = build.JobId!.Value;
        var job = sim.JobManager.ActiveJobs.Single(candidate => candidate.Id == jobId);
        var first = sim.Crew[0].Crew;
        sim.JobManager.ClaimHighestPriorityJob(first);
        Assert.Equal(JobState.Reserved, job.State);

        var reassign = sim.ExecuteCommand(new ReassignJobCommand(jobId));

        Assert.True(reassign.Success, reassign.Message);
        Assert.Equal(JobState.Queued, job.State);
        Assert.Null(job.AssignedCrewId);
        Assert.True(sim.JobManager.CanCrewClaimJob(job, first));
    }

    [Fact]
    public void TransferCoolant_ValidatesReserveAndCapacity()
    {
        SimulationState sim = CommandTestWorld.Create();
        sim.PlumbingSolver.CoolantStoredLiters = 50.0;
        var transfer = sim.ExecuteCommand(new TransferCoolantCommand(10.0));
        Assert.True(transfer.Success, transfer.Message);
        Assert.Equal(60.0, sim.PlumbingSolver.CoolantStoredLiters, precision: 8);

        var fill = sim.ExecuteCommand(new TransferCoolantCommand(1000.0));
        Assert.True(fill.Success, fill.Message);
        var full = sim.ExecuteCommand(new TransferCoolantCommand(10.0));
        Assert.False(full.Success);
        Assert.Equal("NoCapacity", full.FailureCode);

        sim.PlumbingSolver.CoolantReserveLiters = 0.0;
        var empty = sim.ExecuteCommand(new TransferCoolantCommand(10.0));
        Assert.False(empty.Success);
        Assert.Equal("NoCoolantReserve", empty.FailureCode);
    }

    [Fact]
    public void CommandOutcomes_AreRecordedInEventHistory()
    {
        SimulationState sim = CommandTestWorld.Create();
        GridCoord target = CommandTestWorld.OpenHullTile(sim);

        sim.ExecuteCommand(new BuildTileCommand(target, TileType.Floor, 1, 3.0));
        sim.ExecuteCommand(new BuildTileCommand(new GridCoord(99, 99), TileType.Floor, 1, 3.0));

        Assert.Contains(sim.Events.Recent, entry => entry.Kind == SimulationEventKind.CommandApplied);
        Assert.Contains(sim.Events.Recent, entry => entry.Kind == SimulationEventKind.CommandRejected);
        Assert.Contains(sim.Events.Recent, entry => entry.Message.Contains("InvalidCoordinate"));
    }
}
