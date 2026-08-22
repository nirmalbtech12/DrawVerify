using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Models;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LEMUAgenticPipeline.OpenAI;

/// <summary>
/// Invokes the OpenAI Responses API. Builds the same pipeline prompt and stores
/// the same per-story artifacts as <see cref="OpenCode.OpenCodeAgentService"/>.
/// </summary>
public sealed class OpenAIAgentService : ICodeAgentService
{
    private readonly PipelineOptions _opts;
    private readonly FileStateStore _store;
    private readonly ILogger<OpenAIAgentService> _log;
    private readonly HttpClient _httpClient;
    private readonly string _systemPrompt;

    public OpenAIAgentService(
        IOptions<PipelineOptions> opts,
        FileStateStore store,
        ILogger<OpenAIAgentService> log,
        HttpClient httpClient)
    {
        _opts = opts.Value;
        _store = store;
        _log = log;
        _httpClient = httpClient;
        _systemPrompt = LoadSystemPrompt();
    }

    public string BuildPrompt(
        AgentType agentType,
        StoryContext storyContext,
        string promptTemplate,
        Dictionary<string, string>? customInputs)
    {
        var agentDefinitionPath = Path.Combine(_opts.Paths.AgentDir, $"{ToKebab(agentType)}.md");
        var agentDefinition = File.Exists(agentDefinitionPath)
            ? File.ReadAllText(agentDefinitionPath)
            : $"# Agent: {agentType}";

        var inputs = new Dictionary<string, string>
        {
            ["jira_key"] = storyContext.StoryKey,
            ["jira_summary"] = storyContext.Summary,
            ["requirement"] = storyContext.Requirement,
            ["acceptance_criteria"] = string.Join("\n", storyContext.AcceptanceCriteria.Select((criterion, index) => $"{index + 1}. {criterion}")),
            ["epic"] = storyContext.EpicKey,
            ["feature"] = storyContext.FeatureKey,
            ["technical_design"] = storyContext.TechnicalDesignContent ?? "(not yet approved)",
            ["test_design"] = storyContext.TestDesignContent ?? "(not yet approved)",
        };
        if (customInputs != null)
            foreach (var input in customInputs)
                inputs[input.Key] = input.Value;

        var renderedPrompt = promptTemplate;
        foreach (var input in inputs)
            renderedPrompt = renderedPrompt.Replace($"{{{{{input.Key}}}}}", input.Value, StringComparison.Ordinal);

        var model = _opts.ModelRouting.GetModel(agentType);
        return $"""
                {_systemPrompt}

                ---
                # Agent Definition
                {agentDefinition}

                ---
                # Task Prompt
                {renderedPrompt}

                ---
                # Orchestrator Metadata
                Model: {model}
                Story: {storyContext.StoryKey}
                Agent: {agentType}
                """;
    }

    public async Task<AgentResult> InvokeAgentAsync(
        AgentType agentType,
        StoryContext storyContext,
        string promptFilePath,
        Dictionary<string, string>? customInputs = null)
    {
        var apiKey = ResolveApiKey();
        var model = _opts.ModelRouting.GetModel(agentType);
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException($"No OpenAI model is configured for {agentType}.");

        var promptTemplate = await File.ReadAllTextAsync(promptFilePath);
        var fullPrompt = BuildPrompt(agentType, storyContext, promptTemplate, customInputs);
        await _store.SaveArtifactAsync(storyContext.StoryKey, $"{ToKebab(agentType)}-prompt.md", fullPrompt);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildResponsesUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model,
                input = fullPrompt,
                store = _opts.OpenAI.StoreResponses,
            }),
            Encoding.UTF8,
            "application/json");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_opts.OpenAI.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            _log.LogInformation("Invoking OpenAI agent {Agent} model {Model} for {Story}", agentType, model, storyContext.StoryKey);
            response = await _httpClient.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenAI agent {agentType} timed out after {_opts.OpenAI.TimeoutSeconds}s.", ex);
        }

        using (response)
        {
            var requestId = response.Headers.TryGetValues("x-request-id", out var requestIds)
                ? requestIds.FirstOrDefault()
                : null;
            var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogError(
                    "OpenAI Responses API failed for {Agent} and {Story}: status {StatusCode}, request {RequestId}",
                    agentType,
                    storyContext.StoryKey,
                    (int)response.StatusCode,
                    requestId ?? "unavailable");
                throw new InvalidOperationException(
                    $"OpenAI Responses API returned {(int)response.StatusCode} ({response.ReasonPhrase}). Request ID: {requestId ?? "unavailable"}.");
            }

            var output = ExtractOutput(responseBody, requestId);
            var isValid = ValidateOutput(agentType, output);
            var outputPath = await StoreOutputAsync(storyContext, agentType, output);
            return new AgentResult
            {
                AgentType = agentType,
                Output = output,
                OutputPath = outputPath,
                IsValid = isValid,
                Status = isValid ? "PASS" : "INVALID_SCHEMA",
            };
        }
    }

    public bool ValidateOutput(AgentType agentType, string output)
    {
        var schemaFile = agentType switch
        {
            AgentType.TechnicalDesigner => "technical-design.schema.json",
            AgentType.TestDesigner => "test-design.schema.json",
            AgentType.Implementer => "implementation-result.schema.json",
            AgentType.Reviewer => "review-result.schema.json",
            _ => null,
        };
        if (schemaFile == null)
            return !string.IsNullOrWhiteSpace(output);

        var schemaPath = Path.Combine(_opts.Paths.SchemaDir, schemaFile);
        if (!File.Exists(schemaPath))
            return true;

        if (agentType is AgentType.Implementer or AgentType.Reviewer)
        {
            try
            {
                JsonDocument.Parse(output);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        var requiredHeadings = agentType == AgentType.TechnicalDesigner
            ? new[] { "## Jira", "## Requirement", "## Proposed Changes", "## Risks", "## Acceptance Criteria Mapping" }
            : new[] { "## Jira", "## Test Strategy", "## Unit Tests", "## Acceptance Criteria" };
        return requiredHeadings.All(heading => output.Contains(heading, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> StoreOutputAsync(StoryContext storyContext, AgentType agentType, string output)
    {
        var fileName = agentType switch
        {
            AgentType.TechnicalDesigner => "technical-design.md",
            AgentType.TestDesigner => "test-design.md",
            AgentType.Implementer => "implementation-result.json",
            AgentType.Verifier => "verification-result.md",
            AgentType.Reviewer => "review-result.json",
            _ => $"{ToKebab(agentType)}.md",
        };
        await _store.SaveArtifactAsync(storyContext.StoryKey, fileName, output);
        return Path.Combine(_store.StoryDir(storyContext.StoryKey), fileName);
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_opts.OpenAI.ApiKey))
            return _opts.OpenAI.ApiKey;

        var apiKey = Environment.GetEnvironmentVariable(_opts.OpenAI.ApiKeyEnv);
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        throw new InvalidOperationException(
            $"OpenAI API key is not configured. Set Pipeline:OpenAI:ApiKey through a secure configuration provider or environment variable {_opts.OpenAI.ApiKeyEnv}.");
    }

    private Uri BuildResponsesUri()
    {
        if (!Uri.TryCreate(_opts.OpenAI.BaseUrl, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"OpenAI base URL is invalid: {_opts.OpenAI.BaseUrl}");

        return new Uri(baseUri, "responses");
    }

    private static string ExtractOutput(string responseBody, string? requestId)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(outputText.GetString()))
            return outputText.GetString()!;

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var text = new StringBuilder();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                        part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                        text.Append(partText.GetString());
                }
            }

            if (text.Length > 0)
                return text.ToString();
        }

        throw new InvalidOperationException($"OpenAI Responses API returned no output text. Request ID: {requestId ?? "unavailable"}.");
    }

    private static string ToKebab(AgentType agentType) => agentType switch
    {
        AgentType.TechnicalDesigner => "technical-designer",
        AgentType.TestDesigner => "test-designer",
        AgentType.Implementer => "implementer",
        AgentType.Verifier => "verifier",
        AgentType.Reviewer => "reviewer",
        _ => agentType.ToString().ToLowerInvariant(),
    };

    private static string LoadSystemPrompt()
    {
        const string path = ".pipeline/prompts/system.md";
        if (File.Exists(path))
            return File.ReadAllText(path);

        return """
               You are an AI assistant participating in the DrawVerify Agentic Development Pipeline.
               Deterministic orchestration, AI-assisted reasoning, human-controlled approval.
               You never control workflow state, Git, Jira, merges, or deployments.
               You only generate the artifact requested for your agent role, in the exact schema required.
               """;
    }
}
