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

            var rawOutput = ExtractOutput(responseBody, requestId);
            // Deterministic: read <response> block, parse JSON, validate, write via existing file mechanism
            string responseJson = string.Empty;
            string extractError = string.Empty;
            bool hasResponseBlock = TryExtractResponseBlock(rawOutput, out responseJson, out extractError);

            string status;
            string output;
            string outputPath;
            bool isValid;

            if (!hasResponseBlock)
            {
                var errorMsg = $"Missing or invalid <response> block: {extractError}. Expected <response>{{\"status\":\"SUCCESS|FAILURE\", \"response\":\"...\", \"reason\":\"...\"}}</response>";
                _log.LogError("Missing <response> for {Agent} {Story}: {Error}. Raw snippet: {Snippet}", agentType, storyContext.StoryKey, extractError, rawOutput.Length > 500 ? rawOutput[..500] + "..." : rawOutput);
                output = errorMsg;
                status = "INVALID_RESPONSE";
                isValid = false;
                outputPath = await StoreOutputAsync(storyContext, agentType, errorMsg);
            }
            else if (!TryParseProjectResponse(responseJson, out var parsedStatus, out var parsedResponse, out var parsedReason, out var parseError))
            {
                var errorMsg = $"Invalid <response> JSON: {parseError}. Raw: {(responseJson.Length > 500 ? responseJson[..500] + "..." : responseJson)}";
                _log.LogError("Invalid <response> JSON for {Agent} {Story}: {Error}", agentType, storyContext.StoryKey, parseError);
                output = errorMsg;
                status = "INVALID_RESPONSE";
                isValid = false;
                outputPath = await StoreOutputAsync(storyContext, agentType, errorMsg);
            }
            else if (parsedStatus == "SUCCESS")
            {
                if (!ValidateOutput(agentType, parsedResponse))
                {
                    var missing = agentType is AgentType.TechnicalDesigner or AgentType.TestDesigner
                        ? string.Join(", ", GetMissingRequiredSections(agentType, parsedResponse))
                        : "output is not valid JSON";
                    var errorMsg = $"SUCCESS response failed data contract validation: {missing}";
                    _log.LogError("Inner validation failed for {Agent} {Story}: {Error}. Response snippet: {Snippet}", agentType, storyContext.StoryKey, missing, parsedResponse.Length > 500 ? parsedResponse[..500] + "..." : parsedResponse);
                    output = errorMsg;
                    status = "INVALID_RESPONSE";
                    isValid = false;
                    outputPath = await StoreOutputAsync(storyContext, agentType, errorMsg);
                }
                else
                {
                    output = parsedResponse;
                    status = "SUCCESS";
                    isValid = true;
                    outputPath = await StoreOutputAsync(storyContext, agentType, parsedResponse);
                    _log.LogInformation("Wrote SUCCESS response for {Agent} {Story} -> {Path} ({Len} chars)", agentType, storyContext.StoryKey, outputPath, parsedResponse.Length);
                }
            }
            else // FAILURE
            {
                output = parsedReason;
                status = "FAILURE";
                isValid = false;
                _log.LogWarning("Agent {Agent} returned FAILURE for {Story}: {Reason}", agentType, storyContext.StoryKey, parsedReason);
                outputPath = await StoreOutputAsync(storyContext, agentType, parsedReason);
            }

            return new AgentResult
            {
                AgentType = agentType,
                Output = output,
                OutputPath = outputPath,
                IsValid = isValid,
                Status = status
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

    private static bool TryExtractResponseBlock(string output, out string json, out string error)
    {
        json = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            error = "Empty output";
            return false;
        }
        var startTag = "<response>";
        var endTag = "</response>";
        var startIdx = output.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        var endIdx = output.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        if (startIdx == -1 || endIdx == -1 || endIdx <= startIdx)
        {
            error = "Missing <response> block";
            return false;
        }
        startIdx += startTag.Length;
        json = output.Substring(startIdx, endIdx - startIdx).Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Empty <response> block";
            return false;
        }
        return true;
    }

    private static bool TryParseProjectResponse(string json, out string status, out string response, out string reason, out string error)
    {
        status = response = reason = error = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("status", out var statusProp) || statusProp.ValueKind != JsonValueKind.String)
            {
                error = "Missing or invalid 'status'";
                return false;
            }
            status = statusProp.GetString()?.Trim() ?? string.Empty;
            if (status != "SUCCESS" && status != "FAILURE")
            {
                error = $"Invalid status '{status}', must be SUCCESS or FAILURE";
                return false;
            }
            if (status == "SUCCESS")
            {
                if (!root.TryGetProperty("response", out var respProp))
                {
                    error = "SUCCESS requires 'response'";
                    return false;
                }
                if (respProp.ValueKind == JsonValueKind.String)
                    response = respProp.GetString() ?? string.Empty;
                else
                    response = respProp.GetRawText();
                if (string.IsNullOrWhiteSpace(response))
                {
                    error = "SUCCESS response is empty";
                    return false;
                }
            }
            else
            {
                if (!root.TryGetProperty("reason", out var reasonProp) || reasonProp.ValueKind != JsonValueKind.String)
                {
                    error = "FAILURE requires 'reason' string";
                    return false;
                }
                reason = reasonProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(reason))
                {
                    error = "FAILURE reason is empty";
                    return false;
                }
            }
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static IReadOnlyList<string> GetMissingRequiredSections(AgentType agentType, string output)
    {
        var required = agentType == AgentType.TechnicalDesigner
            ? new[] { "## Jira", "## Requirement", "## Proposed Changes", "## Risks", "## Acceptance Criteria Mapping" }
            : new[] { "## Jira", "## Test Strategy", "## Unit Tests", "## Acceptance Criteria" };
        return required.Where(h => !output.Contains(h, StringComparison.OrdinalIgnoreCase)).ToList();
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
