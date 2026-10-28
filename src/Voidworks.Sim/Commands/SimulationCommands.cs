using System;

namespace Voidworks.Sim.Commands;

public enum UtilityKind
{
    PowerCable,
    CoolantPipe,
    PropellantPipe
}

/// <summary>
/// Explicit outcome of a player command. Failures carry a stable code so the UI,
/// tests, and alerts can explain why an action was rejected.
/// </summary>
public sealed record SimulationCommandResult(
    bool Success,
    string Message,
    string? FailureCode = null,
    Guid? JobId = null)
{
    public static SimulationCommandResult Ok(string message, Guid? jobId = null)
        => new(true, message, null, jobId);

    public static SimulationCommandResult Fail(string failureCode, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return new(false, message, failureCode);
    }
}

/// <summary>
/// A validated player action. Commands live in the simulation assembly and must
/// not depend on rendering, input, or windowing code.
/// </summary>
public interface ISimulationCommand
{
    string Name { get; }
    SimulationCommandResult Execute(SimulationState simulation);
}
