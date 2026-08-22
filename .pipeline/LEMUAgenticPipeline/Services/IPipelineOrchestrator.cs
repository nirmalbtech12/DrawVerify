using LEMUAgenticPipeline.Models;

namespace LEMUAgenticPipeline.Services;

/// <summary>
/// Main pipeline orchestrator — drives the state machine and coordinates all integrations.
/// Responsible for:
/// - State machine transitions (only the orchestrator may transition states)
/// - Invoking agents at the right gates
/// - Enforcing safety limits (no auto-merge, max 2 recovery attempts, etc.)
/// - Persisting per-story artifacts under .pipeline/state/<JIRA-ID>/
/// </summary>
public interface IPipelineOrchestrator
{
    /// <summary>Start processing a new Jira Story.</summary>
    /// <param name="storyKey">Jira issue key (e.g., DV-TEST-001).</param>
    /// <returns>StoryContext initialized with Jira data.</Task<StoryContext>
    Task<StoryContext> StartStoryAsync(string storyKey);

    /// <summary>Transition the pipeline to the next state.</summary>
    /// <param name="newState">Target PipelineState.</param>
    /// <returns>True if transition succeeded.</returns>
    Task<bool> TransitionStateAsync(PipelineState newState);

    /// <summary>Current pipeline state for the active story.</summary>
    PipelineState GetCurrentState();

    /// <summary>Check whether the current state requires a human approval gate.</summary>
    bool RequiresHumanApproval();

    /// <summary>Check whether the current state is a failure state that needs diagnosis.</summary>
    bool IsFailureState();

    /// <summary>Get the current StoryContext.</summary>
    StoryContext GetStoryContext();

    /// <summary>Request a human review/approval and wait (blocking).</summary>
    /// <param name="reason">Why human approval is needed.</param>
    /// <returns>Result from human (e.g., "APPROVED", "REWORK").</returns>
    Task<string> RequestHumanApprovalAsync(string reason);

    /// <summary>Skip the current stage (emergency only, logged and flagged).</summary>
    Task SkipStageAsync(string reason);

    /// <summary>Abort the current story pipeline.</summary>
    Task AbortAsync(string reason);

    /// <summary>Get the pipeline state data file path for the current story.</summary>
    string GetStateFilePath();

    /// <summary>Persist the current StoryContext and state to disk.</summary>
    Task SaveStateAsync();

    /// <summary>Recovery: attempt limited repair after a build/test failure (max 2 attempts).</summary>
    /// <param name="attemptNumber">Current repair attempt (1 or 2).</param>
    Task<RecoveryResult> AttemptRecoveryAsync(int attemptNumber);
}

/// <summary>Result of a recovery attempt (limited repair).</summary>
public sealed class RecoveryResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public bool ShouldContinue { get; set; }
    public int MaxAttempts { get; set; } = 2;
}

/// <summary>
/// Safety limits configuration — enforced by the orchestrator, not AI.
/// </summary>
public sealed class SafetyLimits
{
    /// <summary>Maximum auto-repair attempts after build/test failure.</summary>
    public int MaxRecoveryAttempts { get; set; } = 2;

    /// <summary>Auto-merge is always disabled (human review mandatory).</summary>
    public bool AutoMergeDisabled { get; set; } = true;

    /// <summary>Production deployment is disabled in MVP.</summary>
    public bool ProductionDeploymentDisabled { get; set; } = true;

    /// <summary>Jira requirement modification is disabled.</summary>
    public bool JiraModificationDisabled { get; set; } = true;

    /// <summary>Branch deletion is disabled.</summary>
    public bool BranchDeletionDisabled { get; set; } = true;

    /// <summary>Unlimited agent loops are disabled.</summary>
    public bool UnlimitedAgentLoopsDisabled { get; set; } = true;
}