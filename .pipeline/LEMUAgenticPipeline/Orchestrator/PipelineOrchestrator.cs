using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Models;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LEMUAgenticPipeline.Orchestrator;

/// <summary>
/// Deterministic orchestrator. Owns state transitions, enforces safety limits, and coordinates agents.
/// AI is invoked only where reasoning is required; all privileged actions are orchestrator-only.
/// </summary>
public sealed class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IJiraService _jira;
    private readonly IGitService _git;
    private readonly IBuildTestService _buildTest;
    private readonly ICodeAgentService _agents;
    private readonly FileStateStore _store;
    private readonly PipelineOptions _opts;
    private readonly ILogger<PipelineOrchestrator> _log;

    private PipelineState _current = PipelineState.Discovered;
    private StoryContext _ctx = new();
    private int _recoveryAttempts;

    public PipelineOrchestrator(
        IJiraService jira,
        IGitService git,
        IBuildTestService buildTest,
        ICodeAgentService agents,
        FileStateStore store,
        IOptions<PipelineOptions> opts,
        ILogger<PipelineOrchestrator> log)
    {
        _jira = jira;
        _git = git;
        _buildTest = buildTest;
        _agents = agents;
        _store = store;
        _opts = opts.Value;
        _log = log;
    }

    public PipelineState GetCurrentState() => _current;
    public StoryContext GetStoryContext() => _ctx;
    public bool RequiresHumanApproval() => StateMachine.RequiresHumanApproval(_current);
    public bool IsFailureState() => StateMachine.IsFailure(_current);
    public string GetStateFilePath() => _store.StateFile(_ctx.StoryKey);
    public async Task SaveStateAsync() => await _store.SaveStateAsync(_ctx.StoryKey, new PipelineStateData { State = _current, CurrentStoryKey = _ctx.StoryKey });

    public async Task<StoryContext> StartStoryAsync(string storyKey)
    {
        _ctx = await _jira.GetStoryAsync(storyKey);
        _ctx.StoryKey = storyKey;
        await _store.EnsureStoryDirAsync(storyKey);
        await _store.SaveArtifactAsync(storyKey, "jira.json", System.Text.Json.JsonSerializer.Serialize(_ctx, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        _current = PipelineState.Discovered;
        await PersistTransitionAsync("Discovered story", _current);
        return _ctx;
    }

    public async Task<bool> TransitionStateAsync(PipelineState next)
    {
        StateMachine.ValidateTransition(_current, next);
        _current = next;
        await PersistTransitionAsync($"Transition -> {next}", next);
        // Safety: if entering a human gate, STOP — do not auto-advance
        if (StateMachine.IsHumanGate(next))
            _log.LogWarning("HUMAN GATE reached: {State} for {Story} — awaiting approval", next, _ctx.StoryKey);
        return true;
    }

    public async Task<string> RequestHumanApprovalAsync(string reason)
    {
        // Orchestrator blocks here. In CLI this prints and waits for key/file; in service it polls a queue/DB.
        // Approval is per-agent (e.g., technical-design-approval.json) and bound to its JIRA-specific source artifact (e.g., technical-design.md).
        // The source artifact itself (technical-design.md / test-design.md) is the content to be validated — no separate approval-request.md is needed.
        var agentId = FileStateStore.AgentIdForState(_current);
        var sourceArtifact = FileStateStore.SourceArtifactForState(_current);
        _log.LogWarning("Human approval required for {Story} at {State} (agent: {Agent}, source: {Source}): {Reason}", _ctx.StoryKey, _current, agentId, sourceArtifact ?? "none", reason);
        // Create per-agent pending approval file so system has its own file per JIRA and per human-approved agent
        var pendingPayload = new { decision = "PENDING", state = _current.ToString(), agent = agentId, sourceArtifact, at = DateTime.UtcNow };
        var pendingJson = System.Text.Json.JsonSerializer.Serialize(pendingPayload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var pendingFile = System.IO.Path.GetFileName(_store.ApprovalFile(_ctx.StoryKey, agentId));
        await _store.SaveArtifactAsync(_ctx.StoryKey, pendingFile, pendingJson);
        _log.LogInformation("Awaiting {Agent}-approval.json for {Story} (source: {Source}) — created {File} with PENDING, approve via: pipeline approve {Story} --state {State} --decision APPROVED", agentId, _ctx.StoryKey, sourceArtifact ?? "none", pendingFile, _ctx.StoryKey, _current);
        // MVP: read from .pipeline/state/<key>/<agent>-approval.json written by human/CLI (overwrites PENDING)
        var decision = await PollApprovalAsync();
        // On APPROVED, sync approved design markdown to Jira custom field (per human review) — configurable via Pipeline:Jira:ImplementationField / TestDesignField
        if (string.Equals(decision, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            var jiraField = _opts.Jira.GetFieldForAgent(agentId) ?? FileStateStore.JiraFieldForAgent(agentId);
            if (jiraField != null && sourceArtifact != null)
            {
                var mdContent = await _store.LoadArtifactAsync(_ctx.StoryKey, sourceArtifact);
                if (!string.IsNullOrWhiteSpace(mdContent))
                {
                    try
                    {
                        await _jira.UpdateIssueFieldAsync(_ctx.StoryKey, jiraField, mdContent);
                        _log.LogInformation("Updated Jira {Jira} field {Field} ({Label}) for {Agent} approval via orchestrator", _ctx.StoryKey, jiraField, FileStateStore.JiraFieldLabelForAgent(agentId), agentId);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to update Jira {Jira} field {Field} for {Agent} approval via orchestrator — approval still recorded", _ctx.StoryKey, jiraField, agentId);
                    }
                }
            }
        }
        return decision;
    }

    public Task SkipStageAsync(string reason)
    {
        if (_opts.Safety.UnlimitedAgentLoopsDisabled)
            throw new InvalidOperationException("SkipStage is disabled by safety limits");
        _log.LogWarning("SkipStage requested: {Reason}", reason);
        return Task.CompletedTask;
    }

    public Task AbortAsync(string reason)
    {
        _log.LogError("Abort {Story}: {Reason}", _ctx.StoryKey, reason);
        _current = PipelineState.Blocked;
        return _store.SaveStateAsync(_ctx.StoryKey, new PipelineStateData { State = _current, CurrentStoryKey = _ctx.StoryKey });
    }

    public async Task<RecoveryResult> AttemptRecoveryAsync(int attemptNumber)
    {
        // SAFETY: max 2 attempts, only after TEST_FAILED/BUILD_FAILED, no auto-repair in first 3 stories (MVP)
        if (_opts.Safety.MaxRecoveryAttempts == 0)
            return new RecoveryResult { Success = false, ShouldContinue = false, Output = "Auto-recovery disabled (MVP safety)" };
        if (attemptNumber > _opts.Safety.MaxRecoveryAttempts)
            return new RecoveryResult { Success = false, ShouldContinue = false, Output = $"Max recovery attempts ({_opts.Safety.MaxRecoveryAttempts}) exceeded" };
        if (_current is not PipelineState.BuildFailed and not PipelineState.TestFailed and not PipelineState.Verification)
            return new RecoveryResult { Success = false, ShouldContinue = false, Output = $"Recovery not applicable in state {_current}" };

        _recoveryAttempts = attemptNumber;
        _log.LogInformation("Recovery attempt {Attempt}/{Max} for {Story}", attemptNumber, _opts.Safety.MaxRecoveryAttempts, _ctx.StoryKey);
        // MVP: no automatic fix — delegate to verifier agent for diagnosis only, then human
        var verifierResult = await _agents.InvokeAgentAsync(AgentType.Verifier, _ctx,
            Path.Combine(_opts.Paths.PromptDir, "verification.md"),
            new Dictionary<string, string> { ["attempt"] = attemptNumber.ToString() });
        await _store.SaveArtifactAsync(_ctx.StoryKey, $"verification-attempt-{attemptNumber}.md", verifierResult.Output);
        return new RecoveryResult { Success = false, ShouldContinue = false, Output = verifierResult.Output }; // always escalate to human in MVP
    }

    // ---- helpers ----

    private async Task PersistTransitionAsync(string action, PipelineState state)
    {
        var entry = new ExecutionLogEntry { State = state, Action = action, CorrelationId = _ctx.StoryKey };
        await _store.AppendLogAsync(_ctx.StoryKey, entry);
        await _store.SaveStateAsync(_ctx.StoryKey, new PipelineStateData { State = state, CurrentStoryKey = _ctx.StoryKey, LastTransitionedAt = DateTime.UtcNow });
    }

    private async Task<string> PollApprovalAsync()
    {
        // Poll for .pipeline/state/<key>/<agent>-approval.json (e.g., technical-design-approval.json)
        // Each human-approved agent per config Pipeline:Agents:RequiresHumanApproval has its own file per JIRA
        var agentId = FileStateStore.AgentIdForState(_current);
        var approvalFile = _store.ApprovalFile(_ctx.StoryKey, agentId);
        for (int i = 0; i < 600; i++) // 10 min poll, in prod use watcher/queue
        {
            if (File.Exists(approvalFile))
            {
                var json = await File.ReadAllTextAsync(approvalFile);
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    var decision = doc.RootElement.TryGetProperty("decision", out var d) ? d.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(decision)) return decision!;
                    return json.Trim();
                }
                catch { return json.Trim(); }
            }
            await Task.Delay(1000);
        }
        return "TIMEOUT";
    }
}
