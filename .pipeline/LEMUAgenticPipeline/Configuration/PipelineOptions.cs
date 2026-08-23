using LEMUAgenticPipeline.Models;
using LEMUAgenticPipeline.Services;

namespace LEMUAgenticPipeline.Configuration;

/// <summary>
/// Binds to appsettings.json section "Pipeline". Never hard-codes model names in workflow.
/// Example appsettings.json:
/// {
///   "Pipeline": {
///     "Provider": "OpenCode",
///     "Prompts": { "SystemPromptPath": ".pipeline/prompts/system.md" },
///     "ModelRouting": {
///       "TechnicalDesigner": "opencode/nemotron-3-ultra-free",
///       "TestDesigner": "opencode/nemotron-3-ultra-free",
///       "Implementer": "opencode/nemotron-3-ultra-free",
///       "Verifier": "opencode/nemotron-3-ultra-free",
///       "Reviewer": "opencode/nemotron-3-ultra-free",
///       "ComplexRecovery": "opencode/nemotron-3-ultra-free"
///     },
///     "Safety": {
///       "MaxRecoveryAttempts": 2,
///       "AutoMergeDisabled": true,
///       "ProductionDeploymentDisabled": true,
///       "JiraModificationDisabled": true
///     },
///     "Paths": {
///       "Solution": "DrawVerify.sln",
///       "StateRoot": ".pipeline/state",
///       "AgentDir": ".opencode/agents",
///       "PromptDir": ".pipeline/prompts",
///       "SchemaDir": ".pipeline/schemas"
///     },
///     "OpenCode": {
///       "Binary": "opencode",
///       "TimeoutSeconds": 600,
///       "ExtraArgs": ""
///     }
///   }
/// }
/// Override via env: Pipeline__ModelRouting__TechnicalDesigner=...
/// </summary>
public sealed class PipelineOptions
{
    /// <summary>Agent provider registered by pipeline hosts: OpenCode or OpenAI.</summary>
    public string Provider { get; set; } = "OpenCode";
    public PromptsOptions Prompts { get; set; } = new();
    public ModelRoutingOptions ModelRouting { get; set; } = new();
    public SafetyLimits Safety { get; set; } = new();
    public PathOptions Paths { get; set; } = new();
    public OpenCodeOptions OpenCode { get; set; } = new();
    public OpenAIOptions OpenAI { get; set; } = new();
    public JiraOptions Jira { get; set; } = new();
}

public sealed class PromptsOptions
{
    /// <summary>Path to the system prompt file. Defaults to .pipeline/prompts/system.md</summary>
    public string? SystemPromptPath { get; set; } = ".pipeline/prompts/system.md";
}

public sealed class ModelRoutingOptions
{
    /// <summary>Model for TechnicalDesigner — must be provider/model (e.g., opencode/nemotron-3-ultra-free). No default; must be configured.</summary>
    public string TechnicalDesigner { get; set; } = string.Empty;

    /// <summary>Model for TestDesigner.</summary>
    public string TestDesigner { get; set; } = string.Empty;

    /// <summary>Model for Implementer.</summary>
    public string Implementer { get; set; } = string.Empty;

    /// <summary>Model for Verifier.</summary>
    public string Verifier { get; set; } = string.Empty;

    /// <summary>Model for Reviewer.</summary>
    public string Reviewer { get; set; } = string.Empty;

    /// <summary>Model for ComplexRecovery.</summary>
    public string ComplexRecovery { get; set; } = string.Empty;

    /// <summary>Gets model for a specific agent type. Throws if not configured — pipeline stops.</summary>
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
            throw new InvalidOperationException($"Model not configured for {agentType}. Set Pipeline:ModelRouting:{agentType} in appsettings.json (provider/model format, e.g., opencode/muse-spark-1.2-contributor-free).");
        return model;
    }
}

public sealed class PathOptions
{
    public string Solution { get; set; } = "DrawVerify.sln";
    public string StateRoot { get; set; } = ".pipeline/state";
    public string AgentDir { get; set; } = ".pipeline/agents"; // pipeline owns agents; opencode is just model provider
    public string PromptDir { get; set; } = ".pipeline/prompts";
    public string SchemaDir { get; set; } = ".pipeline/schemas";
}

public sealed class OpenCodeOptions
{
    /// <summary>CLI binary name or path.</summary>
    public string Binary { get; set; } = "opencode";

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>Maximum retries for transient upstream errors (502/503/529, overloaded). 0 = no retry.</summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Additional CLI args (e.g., --format json).</summary>
    public string ExtraArgs { get; set; } = string.Empty;

    /// <summary>
    /// Optional URL of a running `opencode serve` instance (e.g., http://localhost:4096).
    /// If set, the pipeline will use `--attach <url>` to reuse the server session,
    /// avoiding MCP cold starts. If not set, runs standalone `opencode run`.
    /// </summary>
    public string? AttachUrl { get; set; }
}

public sealed class OpenAIOptions
{
    /// <summary>OpenAI Responses API base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    /// <summary>API key supplied through user secrets or another secure configuration provider.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Environment variable used when ApiKey is not configured.</summary>
    public string ApiKeyEnv { get; set; } = "OPENAI_API_KEY";

    /// <summary>Maximum duration of an individual Responses API call.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>Whether OpenAI should retain response state. Defaults to false.</summary>
    public bool StoreResponses { get; set; }
}

public sealed class JiraOptions
{
    /// <summary>Jira Cloud base URL, e.g. https://my-site.atlassian.net</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Token value (set via dotnet user-secrets: Pipeline:Jira:Token). Takes precedence over TokenEnv.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Env var name holding the PAT/bearer token (fallback if Token empty, never store token in json).</summary>
    public string TokenEnv { get; set; } = "JIRA_TOKEN";

    /// <summary>Optional: explicit email for basic auth (if needed). Leave empty for bearer.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Jira project key for validation (e.g. DV).</summary>
    public string ProjectKey { get; set; } = string.Empty;

    /// <summary>Custom field IDs for acceptance criteria (if used), e.g. ["customfield_10001"]</summary>
    public string[] AcceptanceCriteriaFields { get; set; } = ["customfield_10001", "customfield_10002"];

    /// <summary>Jira custom field for Implementation (technical design markdown), e.g. customfield_10168.</summary>
    public string ImplementationField { get; set; } = "customfield_10168";

    /// <summary>Jira custom field for Test Design markdown, e.g. customfield_10201.</summary>
    public string TestDesignField { get; set; } = "customfield_10201";

    /// <summary>Request timeout seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Resolve Jira custom field for an agent (technical-design/test-design).</summary>
    public string? GetFieldForAgent(string agentId) => agentId.Trim().ToLowerInvariant() switch
    {
        "technical-design" => string.IsNullOrWhiteSpace(ImplementationField) ? null : ImplementationField,
        "test-design" => string.IsNullOrWhiteSpace(TestDesignField) ? null : TestDesignField,
        _ => null
    };
}

public sealed class JiraServiceException : Exception
{
    public int? StatusCode { get; }
    public string? IssueKey { get; }
    public JiraServiceException(string message, int? statusCode = null, string? issueKey = null, Exception? inner = null)
        : base(message, inner) { StatusCode = statusCode; IssueKey = issueKey; }
}
