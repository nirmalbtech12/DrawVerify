using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Models;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LEMUAgenticPipeline.OpenCode;

/// <summary>
/// Invokes OpenCode CLI as external process. Builds prompt = system + agent md + inputs.
/// System prompt is injected by orchestrator on every call (never delegated to agent).
/// 
/// CLI reference: https://opencode.ai/docs/cli/
/// 
/// Usage:
///   opencode run "message" --model provider/model --agent <agent-name>
///   Optional: --attach <url> to connect to a running `opencode serve` instance.
///   Optional: --dir <path> to set working directory; --auto to auto-approve permissions.
/// 
/// The orchestrator builds the full prompt (system + agent definition + task template + metadata).
/// </summary>
public sealed class OpenCodeAgentService : ICodeAgentService
{
    private readonly PipelineOptions _opts;
    private readonly FileStateStore _store;
    private readonly ILogger<OpenCodeAgentService> _log;
    private readonly string _systemPrompt;

    public OpenCodeAgentService(
        IOptions<PipelineOptions> opts,
        FileStateStore store,
        ILogger<OpenCodeAgentService> log)
    {
        _opts = opts.Value;
        _store = store;
        _log = log;

        // System prompt is loaded from configured path; fail fast if missing.
        var path = _opts.Prompts?.SystemPromptPath ?? ".pipeline/prompts/system.md";
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"System prompt not found at '{path}'. Configure Pipeline:Prompts:SystemPromptPath or place the file there.", path);

        _systemPrompt = File.ReadAllText(path);
    }

    public string BuildPrompt(AgentType agentType, StoryContext ctx, string promptTemplate, Dictionary<string, string>? customInputs)
    {
        // 3. Inputs: Jira + approved designs + build outputs + diff
        var inputs = new Dictionary<string, string>
        {
            ["jira_key"] = ctx.StoryKey,
            ["jira_summary"] = ctx.Summary,
            ["requirement"] = ctx.Requirement,
            ["acceptance_criteria"] = string.Join("\n", ctx.AcceptanceCriteria.Select((ac, i) => $"{i + 1}. {ac}")),
            ["epic"] = ctx.EpicKey,
            ["feature"] = ctx.FeatureKey,
            ["technical_design"] = ctx.TechnicalDesignContent ?? "(not yet approved)",
            ["test_design"] = ctx.TestDesignContent ?? "(not yet approved)",
            ["technical_design_approved"] = ctx.TechnicalDesignApproved ? "true" : "false",
            ["test_design_approved"] = ctx.TestDesignApproved ? "true" : "false",
        };

        if (customInputs != null)
            foreach (var kv in customInputs) inputs[kv.Key] = kv.Value;

        // Simple mustache-style substitution for promptTemplate: {{key}}
        var rendered = promptTemplate;
        foreach (var kv in inputs)
            rendered = rendered.Replace($"{{{{{kv.Key}}}}}", kv.Value, StringComparison.Ordinal);

        // Model routing header — from PipelineOptions
        var model = _opts.ModelRouting.GetModel(agentType);

        // Build prompt file content for --file attachment.
        // Does NOT include AGENT_DEFINITION since agent is specified via --agent flag.
        // Contains: SYSTEM_PROMPT + TASK_PROMPT + ORCHESTRATOR_METADATA
        return $"""
                <<<SYSTEM_PROMPT>>>
                {_systemPrompt}
                <<<SYSTEM_PROMPT_END>>>

                <<<TASK_PROMPT>>>
                {rendered}
                <<<TASK_PROMPT_END>>>

                <<<ORCHESTRATOR_METADATA>>>
                Model: {model}
                Story: {ctx.StoryKey}
                Agent: {agentType}
                TechnicalDesignApproved: {ctx.TechnicalDesignApproved}
                TestDesignApproved: {ctx.TestDesignApproved}
                <<<ORCHESTRATOR_METADATA_END>>>
                """;
    }

    public async Task<AgentResult> InvokeAgentAsync(
        AgentType agentType, StoryContext storyContext, string promptFilePath, Dictionary<string, string>? customInputs = null)
    {
        var model = _opts.ModelRouting.GetModel(agentType);
        var promptTemplate = await File.ReadAllTextAsync(promptFilePath);
        var fullPrompt = BuildPrompt(agentType, storyContext, promptTemplate, customInputs);

        // Persist prompt for audit
        var promptArtifact = $"{ToKebab(agentType)}-prompt.md";
        await _store.SaveArtifactAsync(storyContext.StoryKey, promptArtifact, fullPrompt);

        // File-producing agents write their artifact directly to the story dir per agent definition.
        // Remove any stale artifact so only THIS invocation's file can be picked up.
        var artifactFileName = GetArtifactFileName(agentType);
        var artifactPath = artifactFileName != null ? Path.Combine(_store.StoryDir(storyContext.StoryKey), artifactFileName) : null;
        if (artifactPath != null && File.Exists(artifactPath))
        {
            _log.LogInformation("Removing stale artifact {Path} before invoking {Agent} for {Story}", artifactPath, agentType, storyContext.StoryKey);
            File.Delete(artifactPath);
        }

        try
        {
            // CLI per https://opencode.ai/docs/cli/:
            // opencode run "message" --file <prompt-file> --model provider/model --agent <agent-name>
            // Optional: --attach <url> (connect to running `opencode serve` instance for session reuse)
            // Optional: --dir <path> (working directory); --auto (auto-approve permissions)
            // model already fetched at line 110

            var psi = new ProcessStartInfo(_opts.OpenCode.Binary)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = ResolveRepoRoot(),
            };

            var repoRoot = ResolveRepoRoot();
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add($"Execute the {agentType} task using the attached task input.Complete task or throw an error if unable to complete. Do not make any assumptions about the task or its inputs.");
            
            // Attach the prompt file (contains system prompt + task prompt + metadata)
            var promptArtifactPath = Path.Combine(_store.StoryDir(storyContext.StoryKey), promptArtifact);
            psi.ArgumentList.Add("--file");
            psi.ArgumentList.Add(promptArtifactPath);

            if (!string.IsNullOrWhiteSpace(model))
            {
                psi.ArgumentList.Add("--model");
                psi.ArgumentList.Add(model); // model is already provider/model from config
            }
            psi.ArgumentList.Add("--agent");
            psi.ArgumentList.Add(ToKebab(agentType));
            psi.ArgumentList.Add("--dir");
            psi.ArgumentList.Add(repoRoot);
            psi.ArgumentList.Add("--auto");

            // Optional: attach to long-running server to avoid MCP cold starts
            if (!string.IsNullOrWhiteSpace(_opts.OpenCode.AttachUrl))
            {
                psi.ArgumentList.Add("--attach");
                psi.ArgumentList.Add(_opts.OpenCode.AttachUrl);
                _log.LogInformation("Attaching to OpenCode server at {AttachUrl}", _opts.OpenCode.AttachUrl);
            }

            if (!string.IsNullOrWhiteSpace(_opts.OpenCode.ExtraArgs))
            {
                foreach (var a in SplitArgs(_opts.OpenCode.ExtraArgs))
                    psi.ArgumentList.Add(a);
            }

            // Capture the full CLI invocation command for audit/debugging
            var cliArgs = string.Join(" ", psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            var invocationLog = $"{psi.FileName} {cliArgs}";
            var invocationLogPath = Path.Combine(_store.StoryDir(storyContext.StoryKey), "opencode-invocation.log");
            await File.WriteAllTextAsync(invocationLogPath, invocationLog);
            _log.LogInformation("OpenCode invocation logged to {Path}", invocationLogPath);

            _log.LogInformation("Invoking OpenCode agent {Agent} model {Model} for {Story} (AttachUrl={AttachUrl}, MaxRetries={MaxRetries})",
                agentType, model, storyContext.StoryKey, _opts.OpenCode.AttachUrl ?? "none", _opts.OpenCode.MaxRetries);

            // Retry loop for transient upstream errors (502/503/529, "Service temporarily overloaded")
            int maxAttempts = _opts.OpenCode.MaxRetries + 1; // MaxRetries=1 => 2 attempts
            string stdout = string.Empty, stderr = string.Empty;
            int exitCode = -1;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start opencode");
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                var timeout = TimeSpan.FromSeconds(_opts.OpenCode.TimeoutSeconds);
                var completed = await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(timeout));
                if (completed is Task<bool> t ? !t.Result || !proc.HasExited : !proc.HasExited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    throw new TimeoutException($"opencode agent {agentType} timed out after {timeout.TotalSeconds}s");
                }
                stdout = await stdoutTask;
                stderr = await stderrTask;
                exitCode = proc.ExitCode;
                if (exitCode == 0) break;
                var combined = stderr + stdout;
                bool isTransient = combined.Contains("502") || combined.Contains("503") || combined.Contains("529") || combined.Contains("overloaded", StringComparison.OrdinalIgnoreCase) || combined.Contains("Upstream error", StringComparison.OrdinalIgnoreCase) || combined.Contains("temporarily", StringComparison.OrdinalIgnoreCase);
                if (!isTransient || attempt == maxAttempts)
                    throw new InvalidOperationException($"opencode failed (exit {exitCode}): {stderr}\n{stdout}");
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5); // 10s, 20s on retry 1.. 
                _log.LogWarning("Transient upstream error for {Agent} {Story} (attempt {Attempt}/{MaxAttempts}, exit {ExitCode}): {Stderr}. Retrying in {Backoff}s...", agentType, storyContext.StoryKey, attempt, maxAttempts, exitCode, stderr.Trim(), backoff.TotalSeconds);
                await Task.Delay(backoff);
            }
            if (exitCode != 0)
                throw new InvalidOperationException($"opencode failed (exit {exitCode}): {stderr}\n{stdout}");

            // Deterministic response processing: read <response> block, parse JSON, validate, write via existing file mechanism.
            // 1. Extract <response> JSON from stdout (agent output). Fallback to file artifact if stdout missing (for file agents).
            string responseJson = string.Empty;
            string extractError = string.Empty;
            bool hasResponseBlock = TryExtractResponseBlock(stdout, out responseJson, out extractError);
            if (!hasResponseBlock && artifactPath != null && File.Exists(artifactPath))
            {
                // Fallback: check file artifact content for <response> (in case agent wrote JSON to file)
                var fileContentFallback = await File.ReadAllTextAsync(artifactPath);
                hasResponseBlock = TryExtractResponseBlock(fileContentFallback, out responseJson, out extractError);
                if (hasResponseBlock)
                    _log.LogInformation("Extracted <response> from file artifact fallback for {Agent} {Story}", agentType, storyContext.StoryKey);
            }

            string status;
            string output;
            string outputPath;
            bool isValid;
            string innerValidationError = string.Empty;

            if (!hasResponseBlock)
            {
                // Missing/invalid response block -> write error -> Run = FAILURE (deterministic, no LLM)
                var errorMsg = $"Missing or invalid <response> block: {extractError}. Expected <response>{{\"status\":\"SUCCESS|FAILURE\", \"response\":\"...\", \"reason\":\"...\"}}</response>";
                _log.LogError("Missing <response> for {Agent} {Story}: {Error}. Stdout snippet: {Snippet}", agentType, storyContext.StoryKey, extractError, stdout.Length > 500 ? stdout[..500] + "..." : stdout);
                output = errorMsg;
                status = "INVALID_RESPONSE";
                isValid = false;
                // Write error using existing file mechanism
                if (artifactPath != null)
                {
                    await File.WriteAllTextAsync(artifactPath, errorMsg);
                    outputPath = artifactPath;
                }
                else
                {
                    outputPath = await StoreOutputAsync(storyContext, agentType, errorMsg);
                }
            }
            else if (!TryParseProjectResponse(responseJson, out var parsedStatus, out var parsedResponse, out var parsedReason, out var parseError))
            {
                var errorMsg = $"Invalid <response> JSON: {parseError}. Raw: {(responseJson.Length > 500 ? responseJson[..500] + "..." : responseJson)}";
                _log.LogError("Invalid <response> JSON for {Agent} {Story}: {Error}", agentType, storyContext.StoryKey, parseError);
                output = errorMsg;
                status = "INVALID_RESPONSE";
                isValid = false;
                if (artifactPath != null)
                {
                    await File.WriteAllTextAsync(artifactPath, errorMsg);
                    outputPath = artifactPath;
                }
                else
                {
                    outputPath = await StoreOutputAsync(storyContext, agentType, errorMsg);
                }
            }
            else if (parsedStatus == "SUCCESS")
            {
                // Validate inner response against existing data contract (keep same contract, now wrapped)
                if (!ValidateOutput(agentType, parsedResponse))
                {
                    var missing = agentType is AgentType.TechnicalDesigner or AgentType.TestDesigner
                        ? GetMissingRequiredSections(agentType, parsedResponse)
                        : Array.Empty<string>();
                    innerValidationError = missing.Count > 0
                        ? $"missing required sections: {string.Join(", ", missing)}"
                        : "output is not valid JSON";
                    var errorMsg = $"SUCCESS response failed data contract validation: {innerValidationError}";
                    _log.LogError("Inner validation failed for {Agent} {Story}: {Error}. Response snippet: {Snippet}", agentType, storyContext.StoryKey, innerValidationError, parsedResponse.Length > 500 ? parsedResponse[..500] + "..." : parsedResponse);
                    output = errorMsg;
                    status = "INVALID_RESPONSE";
                    isValid = false;
                    if (artifactPath != null)
                    {
                        await File.WriteAllTextAsync(artifactPath, errorMsg);
                        outputPath = artifactPath;
                    }
                    else
                    {
                        outputPath = await StoreOutputAsync(storyContext, agentType, errorMsg);
                    }
                }
                else
                {
                    // SUCCESS → write response → deterministic file write via existing mechanism
                    output = parsedResponse;
                    status = "SUCCESS";
                    isValid = true;
                    if (artifactPath != null)
                    {
                        await File.WriteAllTextAsync(artifactPath, parsedResponse);
                        outputPath = artifactPath;
                        _log.LogInformation("Wrote SUCCESS response to {Path} for {Agent} {Story} ({Len} chars)", artifactPath, agentType, storyContext.StoryKey, parsedResponse.Length);
                    }
                    else
                    {
                        outputPath = await StoreOutputAsync(storyContext, agentType, parsedResponse);
                        _log.LogInformation("Wrote SUCCESS response via StoreOutputAsync for {Agent} {Story} -> {Path}", agentType, storyContext.StoryKey, outputPath);
                    }
                }
            }
            else // FAILURE
            {
                // FAILURE → write reason → deterministic
                output = parsedReason;
                status = "FAILURE";
                isValid = false; // treated as failure for run status, but JSON itself was valid
                _log.LogWarning("Agent {Agent} returned FAILURE for {Story}: {Reason}", agentType, storyContext.StoryKey, parsedReason);
                if (artifactPath != null)
                {
                    await File.WriteAllTextAsync(artifactPath, parsedReason);
                    outputPath = artifactPath;
                }
                else
                {
                    outputPath = await StoreOutputAsync(storyContext, agentType, parsedReason);
                }
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
        catch (Exception ex) when (ex is not TimeoutException && ex is not InvalidOperationException)
        {
            _log.LogError(ex, "OpenCode invocation failed for {Agent}", agentType);
            throw;
        }
    }

    private static IEnumerable<string> SplitArgs(string args)
    {
        // Minimal split on spaces respecting quotes (good enough for ExtraArgs)
        var list = new List<string>();
        var sb = new StringBuilder();
        bool inQ = false;
        foreach (var c in args)
        {
            if (c == '"') { inQ = !inQ; continue; }
            if (c == ' ' && !inQ) { if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); } }
            else sb.Append(c);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }

    public bool ValidateOutput(AgentType agentType, string output)
    {
        // Schema per agent under .pipeline/schemas/
        var schemaFile = agentType switch
        {
            AgentType.TechnicalDesigner => "technical-design.schema.json",
            AgentType.TestDesigner => "test-design.schema.json",
            AgentType.Implementer => "implementation-result.schema.json",
            AgentType.Reviewer => "review-result.schema.json",
            _ => null
        };

        if (schemaFile == null) return !string.IsNullOrWhiteSpace(output); // verifier: free-form md, just non-empty

        var schemaPath = Path.Combine(_opts.Paths.SchemaDir, schemaFile);
        if (!File.Exists(schemaPath)) return true; // schema missing => skip strict validation

        // Minimal check: JSON-parseable when JSON expected; markdown must contain required headings
        if (agentType is AgentType.Implementer or AgentType.Reviewer)
        {
            try { JsonDocument.Parse(output); return true; }
            catch { return false; }
        }

        // technical/test design: markdown must contain required sections
        return GetMissingRequiredSections(agentType, output).Count == 0;
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
                response = respProp.ValueKind == JsonValueKind.String
                    ? respProp.GetString() ?? string.Empty
                    : respProp.GetRawText();
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
            ? new[]
            {
                "## Jira",
                "## Requirement",
                "## Proposed Changes",
                "## Risks",
                "## Acceptance Criteria Mapping"
            }
            : new[] { "## Jira", "## Test Strategy", "## Unit Tests", "## Acceptance Criteria" };

        return required.Where(h => !output.Contains(h, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string? GetArtifactFileName(AgentType agentType) => agentType switch
    {
        AgentType.TechnicalDesigner => "technical-design.md",
        AgentType.TestDesigner => "test-design.md",
        _ => null
    };

    private static string ExtractFinalOutput(AgentType agentType, string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return string.Empty;

        // For JSON agents (Implementer, Reviewer): find last valid JSON object
        if (agentType is AgentType.Implementer or AgentType.Reviewer)
        {
            var lastJson = ExtractLastJsonObject(stdout);
            if (!string.IsNullOrWhiteSpace(lastJson)) return lastJson;
        }

        // For Verifier (free-form markdown): return last substantial markdown block
        if (agentType == AgentType.Verifier)
        {
            var lastMd = ExtractLastMarkdownBlock(stdout);
            if (!string.IsNullOrWhiteSpace(lastMd)) return lastMd;
        }

        // Fallback: return last non-empty line block (heuristic)
        return ExtractLastSubstantialBlock(stdout);
    }

    private static string ExtractLastJsonObject(string text)
    {
        // Scan backwards for balanced braces to find last complete JSON object
        int depth = 0;
        int? start = null;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == '}') { depth++; if (depth == 1) start = i; }
            else if (c == '{') { depth--; if (depth == 0 && start.HasValue) return text.Substring(i, start.Value - i + 1); }
        }
        return string.Empty;
    }

    private static string ExtractLastMarkdownBlock(string text)
    {
        // Find last markdown heading and return from there
        var lines = text.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                return string.Join('\n', lines[i..]).Trim();
            }
        }
        return string.Empty;
    }

    private static string ExtractLastSubstantialBlock(string text)
    {
        // Return last 2000 chars as fallback (likely contains final answer)
        var trimmed = text.Trim();
        return trimmed.Length > 2000 ? trimmed[^2000..] : trimmed;
    }

    public async Task<string> StoreOutputAsync(StoryContext ctx, AgentType agentType, string output)
    {
        var fileName = agentType switch
        {
            AgentType.TechnicalDesigner => "technical-design.md",
            AgentType.TestDesigner => "test-design.md",
            AgentType.Implementer => "implementation-result.json",
            AgentType.Verifier => "verification-result.md",
            AgentType.Reviewer => "review-result.json",
            _ => $"{ToKebab(agentType)}.md"
        };

        await _store.SaveArtifactAsync(ctx.StoryKey, fileName, output);
        return Path.Combine(_store.StoryDir(ctx.StoryKey), fileName);
    }

    private static string ToKebab(AgentType t) => t switch
    {
        AgentType.TechnicalDesigner => "technical-designer",
        AgentType.TestDesigner => "test-designer",
        AgentType.Implementer => "implementer",
        AgentType.Verifier => "verifier",
        AgentType.Reviewer => "reviewer",
        _ => t.ToString().ToLowerInvariant()
    };

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DrawVerify.sln")) && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}