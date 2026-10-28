namespace Voidworks.Sim.Jobs;

/// <summary>
/// Lifecycle of a posted job. Terminal states are <see cref="Failed"/>,
/// <see cref="Cancelled"/>, and <see cref="Completed"/>; terminal jobs are never
/// claimable again unless a helper explicitly revives a failed job.
/// </summary>
public enum JobState
{
    Queued,
    Reserved,
    Active,
    Blocked,
    Failed,
    Cancelled,
    Completed
}

public static class JobStateExtensions
{
    public static bool IsTerminal(this JobState state)
        => state is JobState.Failed or JobState.Cancelled or JobState.Completed;
}
