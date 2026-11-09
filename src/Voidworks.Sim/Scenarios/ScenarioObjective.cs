using System;

namespace Voidworks.Sim.Scenarios;

public sealed class ScenarioObjective
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public bool IsCompleted { get; set; } = false;
    public bool IsFailed { get; set; } = false;
    public string? Status { get; private set; }
    public Func<SimulationState, bool>? Evaluator { get; set; }
    public Func<SimulationState, string>? StatusProvider { get; set; }

    public ScenarioObjective(
        string id,
        string title,
        string description,
        Func<SimulationState, bool>? evaluator = null,
        Func<SimulationState, string>? statusProvider = null)
    {
        Id = id;
        Title = title;
        Description = description;
        Evaluator = evaluator;
        StatusProvider = statusProvider;
    }

    /// <summary>
    /// Evaluates completion every call, but only refreshes the display status when
    /// requested. Completed objectives keep their last status text.
    /// </summary>
    public void Evaluate(SimulationState sim, bool updateStatus = true)
    {
        if (IsCompleted || IsFailed) return;

        if (updateStatus)
            Status = StatusProvider?.Invoke(sim);

        if (Evaluator != null && Evaluator(sim))
        {
            IsCompleted = true;
        }
    }
}
