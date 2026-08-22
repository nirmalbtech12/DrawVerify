namespace LEMUAgenticPipeline.Models;

/// <summary>
/// Pipeline execution state for a single Jira Story.
/// Maps to the state machine described in AGENTIC_PIPELINE_IMPLEMENTATION.md §4.
/// </summary>
public enum PipelineState
{
    /// <summary>Initial state after Jira story is discovered.</summary>
    Discovered,

    /// <summary>Story hierarchy and dependency validation passed.</summary>
    Validated,

    /// <summary>Technical design is required (not all stories need it).</summary>
    TechDesignRequired,

    /// <summary>Technical design artifact is ready for human approval.</summary>
    TechDesignReady,

    /// <summary>Human approval gate #1: Technical Design review.</summary>
    TechDesignApproval,

    /// <summary>Test design is required.</summary>
    TestDesignRequired,

    /// <summary>Test design artifact is ready for human approval.</summary>
    TestDesignReady,

    /// <summary>Human approval gate #2: Test Design review.</summary>
    TestDesignApproval,

    /// <summary>Implementation can begin (designs approved).</summary>
    ImplementationRequired,

    /// <summary>Implementation in progress.</summary>
    Implementing,

    /// <summary>Implementation complete.</summary>
    Implemented,

    /// <summary>Build execution in progress.</summary>
    Building,

    /// <summary>Test execution in progress.</summary>
    Testing,

    /// <summary>Verification in progress (post-build/test failure diagnosis).</summary>
    Verification,

    /// <summary>PR is ready for human review.</summary>
    PRReady,

    /// <summary>GitHub PR created.</summary>
    PRCreated,

    /// <summary>AI code review in progress.</summary>
    CodeReview,

    /// <summary>Human review in progress.</summary>
    HumanReview,

    /// <summary>Story merged into main.</summary>
    Merged,

    /// <summary>Pipeline complete — no more work.</summary>
    Done,

    /// <summary>Build failed — pipeline stopped.</summary>
    BuildFailed,

    /// <summary>Test failed — pipeline stopped.</summary>
    TestFailed,

    /// <summary>Story is blocked and cannot proceed.</summary>
    Blocked
}

/// <summary>
/// Serialized state of a pipeline for persistence/restart.
/// </summary>
public sealed class PipelineStateData
{
    public PipelineState State { get; set; }
    public DateTime LastTransitionedAt { get; set; } = DateTime.UtcNow;
    public string? CurrentStoryKey { get; set; }
    public Dictionary<string, object?> Payload { get; set; } = new();
}

/// <summary>
/// Context passed between pipeline stages and agents.
/// Contains Jira data, approved designs, results, and artifacts.
/// </summary>
public sealed class StoryContext
{
    /// <summary>Jira issue key (e.g., DV-TEST-001).</summary>
    public string StoryKey { get; set; } = string.Empty;

    /// <summary>Jira issue summary.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Epic key this story belongs to.</summary>
    public string EpicKey { get; set; } = string.Empty;

    /// <summary>Feature key this story belongs to.</summary>
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>Requirement text from Jira.</summary>
    public string Requirement { get; set; } = string.Empty;

    /// <summary>Acceptance criteria from Jira.</summary>
    public List<string> AcceptanceCriteria { get; set; } = new();

    /// <summary>Approved technical design markdown.</summary>
    public string? TechnicalDesignPath { get; set; }

    /// <summary>Approved test design markdown.</summary>
    public string? TestDesignPath { get; set; }

    /// <summary>Implementation result JSON.</summary>
    public string? ImplementationResultPath { get; set; }

    /// <summary>Build result JSON.</summary>
    public string? BuildResultPath { get; set; }

    /// <summary>Test result JSON.</summary>
    public string? TestResultPath { get; set; }

    /// <summary>Code review result JSON.</summary>
    public string? ReviewResultPath { get; set; }

    /// <summary>Execution log entries (chronological).</summary>
    public List<ExecutionLogEntry> ExecutionLog { get; set; } = new();

    /// <summary>Technical design content (for when path is loaded).</summary>
    public string? TechnicalDesignContent { get; set; }

    /// <summary>Test design content (for when path is loaded).</summary>
    public string? TestDesignContent { get; set; }

    /// <summary>Whether technical design has been approved.</summary>
    public bool TechnicalDesignApproved { get; set; }

    /// <summary>Whether test design has been approved.</summary>
    public bool TestDesignApproved { get; set; }
}

/// <summary>
/// Single execution log entry for audit trail.
/// </summary>
public sealed class ExecutionLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public PipelineState State { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Configuration for model routing per agent type.
/// Read from appsettings.json or environment — never hard-coded in workflow logic.
/// </summary>
public sealed class ModelRoutingConfiguration
{
    /// <summary>Model for technical design agent — must be provider/model. No default.</summary>
    public string TechnicalDesigner { get; set; } = string.Empty;

    /// <summary>Model for test design agent.</summary>
    public string TestDesigner { get; set; } = string.Empty;

    /// <summary>Model for implementation agent.</summary>
    public string Implementer { get; set; } = string.Empty;

    /// <summary>Model for verifier agent.</summary>
    public string Verifier { get; set; } = string.Empty;

    /// <summary>Model for code review agent.</summary>
    public string Reviewer { get; set; } = string.Empty;

    /// <summary>Model for complex recovery.</summary>
    public string ComplexRecovery { get; set; } = string.Empty;

    /// <summary>Gets model for a specific agent type. Throws if not configured.</summary>
    public string GetModel(AgentType agentType)
    {
        string? model = agentType switch
        {
            AgentType.TechnicalDesigner => TechnicalDesigner,
            AgentType.TestDesigner => TestDesigner,
            AgentType.Implementer => Implementer,
            AgentType.Verifier => Verifier,
            AgentType.Reviewer => Reviewer,
            AgentType.ComplexRecovery => ComplexRecovery,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException($"Model not configured for {agentType}. Set Pipeline:ModelRouting:{agentType} in appsettings.json (provider/model format).");
        return model;
    }
}

/// <summary>
/// Agent type enum — maps to .opencode/agents/*.md files.
/// </summary>
public enum AgentType
{
    TechnicalDesigner,
    TestDesigner,
    Implementer,
    Verifier,
    Reviewer,
    ComplexRecovery
}

/// <summary>
/// Result of an agent invocation.
/// </summary>
public sealed class AgentResult
{
    /// <summary>Agent type that produced this result.</summary>
    public AgentType AgentType { get; set; }

    /// <summary>Unique invocation ID for tracing.</summary>
    public string InvocationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Full output from the agent (Markdown, JSON, etc.).</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Trace file path where output was stored.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Parsability flag — can the output be deserialized to expected schema?</summary>
    public bool IsValid { get; set; }

    /// <summary>Human-readable status (e.g., "PASS", "FAIL", "PARTIAL").</summary>
    public string Status { get; set; } = "PENDING";
}