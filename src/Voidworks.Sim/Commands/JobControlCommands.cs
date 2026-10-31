using System;
using System.Linq;
using Voidworks.Sim.Jobs;

namespace Voidworks.Sim.Commands;

public sealed record SetJobPriorityCommand(
    Guid JobId,
    int Priority) : ISimulationCommand
{
    public string Name => "SetJobPriority";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Job? job = simulation.JobManager.ActiveJobs.FirstOrDefault(candidate => candidate.Id == JobId);
        if (job == null)
            return SimulationCommandResult.Fail("JobNotFound", $"No active job with id {JobId} exists.");
        if (job.IsTerminal)
            return SimulationCommandResult.Fail("JobTerminal", $"Job '{job.Name}' is already {job.State}.");

        job.Priority = Priority;
        return SimulationCommandResult.Ok($"Job '{job.Name}' priority set to {Priority}.", job.Id);
    }
}

public sealed record CancelJobCommand(Guid JobId) : ISimulationCommand
{
    public string Name => "CancelJob";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Job? job = simulation.JobManager.ActiveJobs.FirstOrDefault(candidate => candidate.Id == JobId);
        if (job == null)
            return SimulationCommandResult.Fail("JobNotFound", $"No active job with id {JobId} exists.");
        if (job.IsTerminal)
            return SimulationCommandResult.Fail("JobTerminal", $"Job '{job.Name}' is already {job.State}.");

        simulation.JobManager.CancelJob(job, "Cancelled by player command.");
        return SimulationCommandResult.Ok($"Cancelled job '{job.Name}'.", job.Id);
    }
}

public sealed record ReassignJobCommand(Guid JobId) : ISimulationCommand
{
    public string Name => "ReassignJob";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        Job? job = simulation.JobManager.ActiveJobs.FirstOrDefault(candidate => candidate.Id == JobId);
        if (job == null)
            return SimulationCommandResult.Fail("JobNotFound", $"No active job with id {JobId} exists.");
        if (job.IsTerminal)
            return SimulationCommandResult.Fail("JobTerminal", $"Job '{job.Name}' is already {job.State}.");

        Guid? previous = job.AssignedCrewId;
        simulation.JobManager.ReleaseJob(job);
        string from = previous.HasValue ? $" from crew {previous}" : " (it was unassigned)";
        return SimulationCommandResult.Ok($"Job '{job.Name}' released{from} and returned to the queue.", job.Id);
    }
}

public sealed record TransferCoolantCommand(double Liters) : ISimulationCommand
{
    public string Name => "TransferCoolant";

    public SimulationCommandResult Execute(SimulationState simulation)
    {
        if (!double.IsFinite(Liters) || Liters <= 0.0)
            return SimulationCommandResult.Fail("InvalidAmount", "Coolant transfer amount must be a positive finite value.");
        if (simulation.PlumbingSolver.CoolantReserveLiters <= 0.0)
            return SimulationCommandResult.Fail("NoCoolantReserve", "The ship's coolant reserve is empty.");

        double transferred = simulation.PlumbingSolver.TransferCoolantFromReserve(Liters);
        return transferred <= 0.0
            ? SimulationCommandResult.Fail("NoCapacity", "The coolant loop is already full.")
            : SimulationCommandResult.Ok($"Transferred {transferred:F1} L of coolant from reserve.");
    }
}
