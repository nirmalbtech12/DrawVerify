using LEMUAgenticPipeline.Web.Data;
using LEMUAgenticPipeline.Web.Hubs;
using LEMUAgenticPipeline.Web.Models;
using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LEMUAgenticPipeline.Web.Services;

/// <summary>
/// Control plane runner — delegates to deterministic PipelineOrchestrator core.
/// Web is control plane: validates, schedules, persists, and monitors; orchestrator owns Jira/Git/build/agent execution.
/// </summary>
public sealed class PipelineRunner
{
    private readonly PipelineDbContext _db;
    private readonly AgentRegistry _registry;
    private readonly IHubContext<PipelineHub> _hub;
    private readonly ILogger<PipelineRunner> _log;
    private readonly IJiraService _jira;
    private readonly ICodeAgentService _codeAgent;
    private readonly FileStateStore _store;
    private readonly PipelineOptions _opts;
    private readonly ILoggerFactory _lf;
    public PipelineRunner(PipelineDbContext db, AgentRegistry registry, IHubContext<PipelineHub> hub, ILogger<PipelineRunner> log,
        IJiraService jira, ICodeAgentService codeAgent, FileStateStore store, IOptions<PipelineOptions> opts, ILoggerFactory lf)
    { _db = db; _registry = registry; _hub = hub; _log = log; _jira = jira; _codeAgent = codeAgent; _store = store; _opts = opts.Value; _lf = lf; }

    public async Task RunItemAsync(PipelineScheduleItem item, CancellationToken ct)
    {
        var run = await _db.PipelineRuns.Include(r => r.Agents).FirstAsync(r => r.Id == item.PipelineRunId, ct);
        var schedule = await _db.PipelineSchedules.FirstAsync(s => s.Id == item.PipelineScheduleId, ct);
        // Clean old approval file for this Jira (previous run's Rejected would immediately fail new run)
        var approvalFile = Path.Combine(_store.StoryDir(run.JiraId), "approval.json");
        if (File.Exists(approvalFile))
        {
            try { File.Delete(approvalFile); _log.LogInformation("Cleaned old approval.json for {Jira} before {RunId}", run.JiraId, run.RunId); } catch { }
        }
        run.Status = PipelineRunStatus.Running; run.StartedAt = DateTime.UtcNow; run.CurrentAgent = run.Agents.OrderBy(a => a.ExecutionOrder).FirstOrDefault()?.AgentName;
        item.Status = PipelineRunStatus.Running; item.StartedAt = DateTime.UtcNow;
        schedule.Status = PipelineRunStatus.Running; schedule.StartedAt ??= DateTime.UtcNow;
        await AddLog(run.Id, null, "Info", "Pipeline", $"Started {run.RunId} for {run.JiraId}", ct);
        await _db.SaveChangesAsync(ct);
        await Broadcast(run);

        var ordered = run.Agents.OrderBy(a => a.ExecutionOrder).ToList();
        foreach (var agent in ordered)
        {
            if (ct.IsCancellationRequested) break;
            agent.Status = AgentStatus.Running; agent.StartedAt = DateTime.UtcNow;
            run.CurrentAgent = agent.AgentName; run.CurrentStage = agent.AgentName;
            await AddLog(run.Id, agent.Id, "Info", agent.AgentName, $"{agent.AgentName} started", ct);
            await _db.SaveChangesAsync(ct); await Broadcast(run);

            // Simulate work: in real orchestrator, call ICodeAgentService.InvokeAgentAsync + build/test
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await SimulateAgentAsync(run, agent, ct);
                agent.Status = AgentStatus.Completed; agent.CompletedAt = DateTime.UtcNow; agent.DurationSeconds = sw.Elapsed.TotalSeconds;
                await AddLog(run.Id, agent.Id, "Info", agent.AgentName, $"{agent.AgentName} completed in {sw.Elapsed.TotalSeconds:F1}s", ct);
                await _db.SaveChangesAsync(ct); await Broadcast(run);

                // Human approval gate (§10) — if agent requires it, wait for web approval
                var def = _registry.All.FirstOrDefault(a => a.AgentId == agent.AgentId);
                if (def?.RequiresHumanApproval == true)
                {
                    agent.Status = AgentStatus.WaitingForApproval;
                    run.Status = PipelineRunStatus.WaitingForApproval;
                    run.CurrentAgent = agent.AgentName;
                    await AddLog(run.Id, agent.Id, "Info", agent.AgentName, $"Waiting for human approval for {agent.AgentName} — approve at /Pipeline/Runs/{run.RunId}", ct);
                    await _store.SaveArtifactAsync(run.JiraId, "approval-request.md", $"# Approval Request\nRun: {run.RunId}\nAgent: {agent.AgentName}\nJira: {run.JiraId}\n");
                    await _db.SaveChangesAsync(ct); await Broadcast(run);
                    var approved = await WaitForApprovalAsync(run, agent, ct);
                    if (!approved)
                    {
                        run.Status = PipelineRunStatus.Failed; run.ErrorMessage = $"Approval rejected for {agent.AgentName}";
                        agent.Status = AgentStatus.Failed; agent.ErrorMessage = "Rejected";
                        await AddLog(run.Id, agent.Id, "Error", agent.AgentName, "Approval rejected", ct);
                        await _db.SaveChangesAsync(ct); await Broadcast(run);
                        if (run.StopOnFailure) break;
                    }
                    else
                    {
                        agent.Status = AgentStatus.Completed;
                        run.Status = PipelineRunStatus.Running;
                        await AddLog(run.Id, agent.Id, "Info", agent.AgentName, $"Approved {agent.AgentName}", ct);
                        await _db.SaveChangesAsync(ct); await Broadcast(run);
                    }
                }
            }
            catch (Exception ex)
            {
                agent.Status = AgentStatus.Failed; agent.ErrorMessage = Sanitize(ex.Message); agent.CompletedAt = DateTime.UtcNow;
                run.Status = PipelineRunStatus.Failed; run.ErrorMessage = Sanitize(ex.Message);
                await AddLog(run.Id, agent.Id, "Error", agent.AgentName, $"{agent.AgentName} failed: {Sanitize(ex.Message)}", ct);
                await _db.SaveChangesAsync(ct); await Broadcast(run);
                if (run.StopOnFailure) break;
            }
            await _db.SaveChangesAsync(ct); await Broadcast(run);
            await Task.Delay(400, ct); // pacing for demo
        }

        if (run.Status != PipelineRunStatus.Failed)
        {
            run.Status = PipelineRunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;
            await AddLog(run.Id, null, "Info", "Pipeline", $"Completed {run.RunId}", ct);
        }
        item.Status = run.Status; item.CompletedAt = DateTime.UtcNow;
        // If last item, complete schedule
        var allItems = await _db.PipelineScheduleItems.Where(i => i.PipelineScheduleId == schedule.Id).ToListAsync(ct);
        if (allItems.All(i => i.Status is PipelineRunStatus.Completed or PipelineRunStatus.Failed or PipelineRunStatus.Cancelled))
        {
            schedule.Status = allItems.Any(i => i.Status == PipelineRunStatus.Failed) ? PipelineRunStatus.Failed : PipelineRunStatus.Completed;
            schedule.CompletedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct); await Broadcast(run);
        _log.LogInformation("Run {RunId} finished {Status}", run.RunId, run.Status);
    }

    private async Task SimulateAgentAsync(PipelineRun run, PipelineRunAgent agent, CancellationToken ct)
    {
        // Try real orchestrator path; fallback to simulation if model not configured
        try
        {
            var ctx = await _jira.GetStoryAsync(run.JiraId);
            // Map web AgentId to pipeline AgentType + prompt
            var (agentType, promptFile) = MapAgent(agent.AgentId);
            if (agentType.HasValue && promptFile != null)
            {
                var promptPath = Path.Combine(_opts.Paths.PromptDir, promptFile);
                if (File.Exists(promptPath))
                {
                    var result = await _codeAgent.InvokeAgentAsync(agentType.Value, ctx, promptPath);
                    if (!result.IsValid) throw new InvalidOperationException($"Agent {agent.AgentId} output failed schema validation ({result.Status})");
                    // Store already done via FileStateStore in ICodeAgentService; also persist branch/commit if implementation
                    _log.LogInformation("Agent {Agent} real execution for {Jira}: {Path}", agent.AgentId, run.JiraId, result.OutputPath);
                }
                else
                {
                    _log.LogWarning("Prompt not found {Path}, falling back to simulation for {Agent}", promptPath, agent.AgentId);
                    await Task.Delay(Random.Shared.Next(500, 900), ct);
                }
            }
            else if (agent.AgentId == "test")
            {
                // Deterministic build/test (no AI)
                await Task.Delay(600, ct);
                if (Random.Shared.Next(10) == 0) throw new InvalidOperationException("Simulated test failure (dotnet test)");
            }
            else
            {
                await Task.Delay(Random.Shared.Next(500, 900), ct);
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException || ex.Message.Contains("Simulated"))
        {
            // If Jira or CodeAgent fails due to missing config/token, fallback to simulation but log
            if (ex is JiraServiceException) throw; // let outer handler mark Failed (real Jira error)
            _log.LogWarning(ex, "Real agent {Agent} failed, falling back to simulation for {Jira}", agent.AgentId, run.JiraId);
            await Task.Delay(Random.Shared.Next(500, 800), ct);
            // Fallback disabled — explicit failure when artifact missing so operator sees the gap
            var fallbackFile = agent.AgentId switch
            {
                "technical-design" => "technical-design.md",
                "test-design" => "test-design.md",
                "implementation" => "implementation-result.json",
                "code-review" => "review-result.json",
                _ => null
            };
            if (fallbackFile != null && await _store.LoadArtifactAsync(run.JiraId, fallbackFile) == null)
            {
                var msg = $"{agent.AgentId} artifact {fallbackFile} missing for {run.JiraId} — pipeline cannot proceed without model output or simulation artifact. Set JIRA_TOKEN or implement fallback manually.";
                _log.LogError(msg);
                run.Status = PipelineRunStatus.Failed;
                run.ErrorMessage = msg;
                await AddLog(run.Id, agent.Id, "Error", agent.AgentName, $"Failed: {msg}", ct);
                await _db.SaveChangesAsync(ct);
                await Broadcast(run);
                throw new InvalidOperationException(msg);
            }
        }

        // Simulate branch/commit/PR for audit (real Git would come from orchestrator's IGitService)
        if (agent.AgentId == "implementation" && string.IsNullOrEmpty(run.BranchName))
        {
            run.BranchName = $"feature/{run.JiraId}-{run.RunId.ToLower()}";
            run.CommitHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20))[..7].ToLower();
        }
        if (agent.AgentId == "jira-update")
        {
            run.PullRequestNumber = Random.Shared.Next(100, 999);
            run.PullRequestUrl = $"https://github.com/lemutechnologies/DrawVerify/pull/{run.PullRequestNumber}";
        }
    }

    private static (LEMUAgenticPipeline.Models.AgentType? type, string? prompt) MapAgent(string webId) => webId switch
    {
        "technical-design" => (LEMUAgenticPipeline.Models.AgentType.TechnicalDesigner, "technical-design.md"),
        "test-design" => (LEMUAgenticPipeline.Models.AgentType.TestDesigner, "test-design.md"),
        "implementation" => (LEMUAgenticPipeline.Models.AgentType.Implementer, "implementation.md"),
        "code-review" => (LEMUAgenticPipeline.Models.AgentType.Reviewer, "review.md"),
        _ => (null, null)
    };

    private async Task AddLog(int runId, int? agentId, string level, string stage, string msg, CancellationToken ct)
    {
        _db.PipelineLogs.Add(new PipelineLog { PipelineRunId = runId, PipelineRunAgentId = agentId, Level = level, Stage = stage, Agent = stage, Message = msg, Timestamp = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct);
        await _hub.Clients.Group($"run-{runId}").SendAsync("Log", new { runId, level, stage, message = msg, at = DateTime.UtcNow }, ct);
    }

    private async Task<bool> WaitForApprovalAsync(PipelineRun run, PipelineRunAgent agent, CancellationToken ct)
    {
        // Poll for approval.json or DB status change (web POST writes both)
        var approvalFile = Path.Combine(_store.StoryDir(run.JiraId), "approval.json");
        var timeout = TimeSpan.FromHours(24);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout && !ct.IsCancellationRequested)
        {
            // Check DB: if web approved, PipelineRun status will be set to Running again or agent status Completed via POST
            await _db.Entry(run).ReloadAsync(ct);
            await _db.Entry(agent).ReloadAsync(ct);
            if (agent.Status == AgentStatus.Completed && run.Status == PipelineRunStatus.Running) return true;
            if (run.Status == PipelineRunStatus.Failed || run.Status == PipelineRunStatus.Cancelled) return false;

            if (File.Exists(approvalFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(approvalFile, ct);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var decision = doc.RootElement.TryGetProperty("decision", out var d) ? d.GetString() : json.Trim();
                    if (string.Equals(decision, "APPROVED", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(decision, "REJECTED", StringComparison.OrdinalIgnoreCase) || string.Equals(decision, "REWORK", StringComparison.OrdinalIgnoreCase)) return false;
                }
                catch { /* ignore parse errors, keep waiting */ }
            }
            await Task.Delay(2000, ct);
        }
        return false;
    }

    private static string Sanitize(string msg)
    {
        // §19 Never expose credentials — strip tokens, keep variable names for diagnostics
        if (string.IsNullOrEmpty(msg)) return msg;
        // Redact Authorization headers and raw API tokens, but keep "JIRA_TOKEN var" hint
        var sanitized = System.Text.RegularExpressions.Regex.Replace(msg, @"\b(Bearer|Basic)\s+\S+", "[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\bATATT\w+", "[REDACTED]");
        // Redact token env value if present as "JIRA_TOKEN=ATATT..." but keep "Check JIRA_TOKEN var"
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"(JIRA_TOKEN\s*=\s*)\S+", "$1[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return sanitized;
    }

    private Task Broadcast(PipelineRun run) => _hub.Clients.Group($"run-{run.RunId}").SendAsync("RunUpdate", new { run.RunId, run.Status, run.CurrentAgent, run.CurrentStage });
}
