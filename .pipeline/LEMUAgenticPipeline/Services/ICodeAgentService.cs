using LEMUAgenticPipeline.Models;

namespace LEMUAgenticPipeline.Services;

/// <summary>
/// Code agent service — LLM-agnostic. Builds prompt (system + agent md + inputs), invokes
/// the configured CLI (OpenCode, Claude, OpenAI, ...), validates and stores output.
/// Implementations: OpenCodeAgentService, ClaudeAgentService, OpenAIAgentService.
/// </summary>
public interface ICodeAgentService
{
    /// <summary>
    /// Invoke an agent with the given inputs and agent type.
    /// </summary>
    /// <param name="agentType">Type of agent to invoke.</param>
    /// <param name="storyContext">Current story context (Jira data, designs, etc.).</param>
    /// <param name="promptFilePath">Path to the agent's prompt template (e.g., prompts/technical-design.md).</param>
    /// <param name="customInputs">Optional additional inputs to merge into the prompt.</param>
    /// <returns>AgentResult containing the output and traceability info.</returns>
    Task<AgentResult> InvokeAgentAsync(AgentType agentType, StoryContext storyContext, string promptFilePath, Dictionary<string, string>? customInputs = null);

    /// <summary>
    /// Build the full prompt for an agent: system prompt + agent prompt + inputs.
    /// </summary>
    /// <param name="agentType">Type of agent.</param>
    /// <param name="storyContext">Current story context.</param>
    /// <param name="promptTemplate">Raw prompt template text.</param>
    /// <param name="customInputs">Additional key-value inputs.</param>
    /// <returns>Complete prompt string ready for OpenCode CLI.</returns>
    string BuildPrompt(AgentType agentType, StoryContext storyContext, string promptTemplate, Dictionary<string, string>? customInputs);

    /// <summary>
    /// Validate that the agent output conforms to the expected schema for the agent type.
    /// </summary>
    /// <param name="agentType">Type of agent that produced the output.</param>
    /// <param name="output">Agent output text.</param>
    /// <returns>True if the output is valid per the expected schema.</returns>
    bool ValidateOutput(AgentType agentType, string output);

    /// <summary>
    /// Store the agent output to the per-story artifacts directory.
    /// </summary>
    /// <param name="storyContext">Current story context (has StoryKey).</param>
    /// <param name="agentType">Type of agent.</param>
    /// <param name="output">Agent output to persist.</param>
    /// <returns>Path where the output was stored.</returns>
    Task<string> StoreOutputAsync(StoryContext storyContext, AgentType agentType, string output);
}

/// <summary>
/// Schema validation results from an agent.
/// </summary>
public sealed class SchemaValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object?> ParsedData { get; set; } = new();
}