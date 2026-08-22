using LEMUAgenticPipeline.Models;

namespace LEMUAgenticPipeline.Orchestrator;

/// <summary>
/// Deterministic state machine. Only the orchestrator may transition states.
/// Human approval gates are explicit states — the machine stops and waits.
/// </summary>
public static class StateMachine
{
    // Allowed transitions: current -> { next[] }
    private static readonly Dictionary<PipelineState, HashSet<PipelineState>> Allowed =
        new()
        {
            [PipelineState.Discovered] = [PipelineState.Validated, PipelineState.Blocked],
            [PipelineState.Validated] = [PipelineState.TechDesignRequired, PipelineState.Blocked],
            [PipelineState.TechDesignRequired] = [PipelineState.TechDesignReady],
            [PipelineState.TechDesignReady] = [PipelineState.TechDesignApproval],
            [PipelineState.TechDesignApproval] = [PipelineState.TestDesignRequired, PipelineState.TechDesignRequired], // approved -> next, rejected -> back
            [PipelineState.TestDesignRequired] = [PipelineState.TestDesignReady],
            [PipelineState.TestDesignReady] = [PipelineState.TestDesignApproval],
            [PipelineState.TestDesignApproval] = [PipelineState.ImplementationRequired, PipelineState.TestDesignRequired],
            [PipelineState.ImplementationRequired] = [PipelineState.Implementing],
            [PipelineState.Implementing] = [PipelineState.Implemented, PipelineState.Blocked],
            [PipelineState.Implemented] = [PipelineState.Building],
            [PipelineState.Building] = [PipelineState.Testing, PipelineState.BuildFailed],
            [PipelineState.Testing] = [PipelineState.Verification, PipelineState.TestFailed, PipelineState.CodeReview], // pass -> review, fail -> verification/failed
            [PipelineState.Verification] = [PipelineState.Building, PipelineState.TestFailed, PipelineState.Blocked], // retry -> building, else fail
            [PipelineState.BuildFailed] = [PipelineState.Verification, PipelineState.Blocked], // MVP: verification -> human, no auto-repair
            [PipelineState.TestFailed] = [PipelineState.Verification, PipelineState.Blocked],
            [PipelineState.CodeReview] = [PipelineState.PRReady, PipelineState.Implementing], // PASS -> PRReady, FAIL -> rework
            [PipelineState.PRReady] = [PipelineState.PRCreated],
            [PipelineState.PRCreated] = [PipelineState.HumanReview],
            [PipelineState.HumanReview] = [PipelineState.Merged, PipelineState.Implementing], // approved -> merged, changes requested -> implementing
            [PipelineState.Merged] = [PipelineState.Done],
            [PipelineState.Done] = [],
            [PipelineState.Blocked] = [PipelineState.Discovered], // after unblock, restart
        };

    // Human approval gates — orchestrator STOPS and waits for external signal
    public static readonly HashSet<PipelineState> HumanGates =
    [
        PipelineState.TechDesignApproval,
        PipelineState.TestDesignApproval,
        PipelineState.HumanReview,
    ];

    public static bool IsHumanGate(PipelineState s) => HumanGates.Contains(s);

    public static bool IsTerminal(PipelineState s) =>
        s is PipelineState.Done or PipelineState.Blocked or PipelineState.BuildFailed or PipelineState.TestFailed;

    public static bool IsFailure(PipelineState s) =>
        s is PipelineState.BuildFailed or PipelineState.TestFailed or PipelineState.Blocked;

    public static bool CanTransition(PipelineState from, PipelineState to)
    {
        return Allowed.TryGetValue(from, out var next) && next.Contains(to);
    }

    public static void ValidateTransition(PipelineState from, PipelineState to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"Illegal transition: {from} -> {to}");
    }

    /// <summary>
    /// Returns true if the orchestrator must halt and wait for human approval before proceeding.
    /// </summary>
    public static bool RequiresHumanApproval(PipelineState current) => IsHumanGate(current);
}
