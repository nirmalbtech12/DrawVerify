using LEMUAgenticPipeline.Models;
using LEMUAgenticPipeline.Services;

namespace LEMUAgenticPipeline.Claude;

/// <summary>Claude CLI provider — implements same <see cref="ICodeAgentService"/> as OpenCode.</summary>
public sealed class ClaudeAgentService : ICodeAgentService
{
    public string BuildPrompt(AgentType t, StoryContext c, string tmpl, Dictionary<string,string>? e) => throw new NotImplementedException("Reuse OpenCodeAgentService.BuildPrompt or extract to shared PromptBuilder");
    public Task<AgentResult> InvokeAgentAsync(AgentType t, StoryContext c, string p, Dictionary<string,string>? e) => throw new NotImplementedException("Invoke: claude -p \"prompt\" --model <mapped> --output-format text");
    public bool ValidateOutput(AgentType t, string o) => !string.IsNullOrWhiteSpace(o);
    public Task<string> StoreOutputAsync(StoryContext c, AgentType t, string o) => throw new NotImplementedException();
}
