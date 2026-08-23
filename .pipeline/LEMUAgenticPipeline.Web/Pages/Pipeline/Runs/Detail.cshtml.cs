using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using LEMUAgenticPipeline.Web.Data;
using LEMUAgenticPipeline.Web.Models;
using LEMUAgenticPipeline.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class DetailModel : PageModel
{
    private readonly PipelineDbContext _db;
    private readonly AgentRegistry _registry;
    private readonly FileStateStore _store;
    private readonly IJiraService _jira;
    private readonly ILogger<DetailModel> _log;
    private readonly JiraOptions _jiraOpts;
    public DetailModel(PipelineDbContext db, AgentRegistry registry, FileStateStore store, IJiraService jira, ILogger<DetailModel> log, IOptions<PipelineOptions> opts){ _db=db; _registry=registry; _store=store; _jira=jira; _log=log; _jiraOpts=opts.Value.Jira; }
    public PipelineRun? Run { get; set; }
    public List<PipelineLog> Logs { get; set; } = [];
    public List<string> NotSelected { get; set; } = [];
    /// <summary>AgentId -> artifact file name (e.g., technical-design -> technical-design.md) if file exists.</summary>
    public Dictionary<string, string> AvailableArtifacts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public async Task OnGetAsync(string runId)
    {
        Run = await _db.PipelineRuns.Include(r=>r.Agents).FirstOrDefaultAsync(r=> r.RunId==runId);
        if(Run==null) return;
        Logs = await _db.PipelineLogs.Where(l=>l.PipelineRunId==Run.Id).OrderBy(l=>l.Timestamp).ToListAsync();
        var selected = Run.Agents.Select(a=>a.AgentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        NotSelected = _registry.All.Where(a=> !selected.Contains(a.AgentId)).Select(a=>a.Name).ToList();
        // Populate view links for agent.md files under .pipeline/state/<JIRA>/
        AvailableArtifacts.Clear();
        foreach (var a in Run.Agents)
        {
            var file = FileStateStore.ArtifactFileForAgent(a.AgentId);
            if (file != null && _store.ArtifactExists(Run.JiraId, a.AgentId))
                AvailableArtifacts[a.AgentId] = file;
        }
    }

    /// <summary>Serve the generated agent artifact (e.g., technical-design.md) from .pipeline/state/&lt;Jira&gt;.</summary>
    public async Task<IActionResult> OnGetArtifactAsync(string runId, string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return BadRequest("agentId required");
        var run = await _db.PipelineRuns.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId);
        if (run == null) return NotFound($"Run {runId} not found");
        agentId = FileStateStore.SanitizeAgentId(agentId);
        var fileName = FileStateStore.ArtifactFileForAgent(agentId);
        if (fileName == null) return NotFound($"No artifact mapping for agent '{agentId}'");
        // Security: only allow the mapped file name, no path traversal
        fileName = Path.GetFileName(fileName);
        var content = await _store.LoadArtifactAsync(run.JiraId, fileName);
        if (content == null) return NotFound($"Artifact {fileName} not found for {run.JiraId}");
        var contentType = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json; charset=utf-8"
            : fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "text/markdown; charset=utf-8"
            : "text/plain; charset=utf-8";
        // Return raw content with inline display; browser will render markdown as text, JSON as text
        return Content(content, contentType);
    }

    public async Task<IActionResult> OnPostApproveAsync(string runId, string decision)
    {
        var run = await _db.PipelineRuns.Include(r=>r.Agents).FirstOrDefaultAsync(r=> r.RunId==runId);
        if(run==null) return NotFound();
        decision = (decision ?? "APPROVED").ToUpperInvariant();
        var waitingAgent = run.Agents.FirstOrDefault(a=> a.Status==AgentStatus.WaitingForApproval);
        var agentId = FileStateStore.SanitizeAgentId(waitingAgent?.AgentId ?? "generic");
        var sourceArtifact = FileStateStore.SourceArtifactForAgent(agentId);
        string? sourceHash = null;
        if (sourceArtifact != null)
        {
            var src = await _store.LoadArtifactAsync(run.JiraId, sourceArtifact);
            if (src != null)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                sourceHash = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(src))).ToLowerInvariant()[..16];
            }
        }
        // Write agent-suffixed approval.json generated FROM the JIRA-specific source artifact — human gate
        // Each human-approved agent gets its own file (e.g., technical-design-approval.json) and polling reads only that file
        var payload = new { decision, runId, agent = agentId, sourceArtifact, sourceHash, at = DateTime.UtcNow };
        var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var fileName = Path.GetFileName(_store.ApprovalFile(run.JiraId, agentId));
        await _store.SaveArtifactAsync(run.JiraId, fileName, json);
        // Also update DB for immediate UI feedback (reuse waitingAgent resolved above)
        if(waitingAgent!=null)
        {
            if(decision=="APPROVED")
            {
                waitingAgent.Status = AgentStatus.Completed;
                run.Status = PipelineRunStatus.Running;
                _db.PipelineLogs.Add(new PipelineLog{ PipelineRunId=run.Id, PipelineRunAgentId=waitingAgent.Id, Level="Info", Stage=waitingAgent.AgentName, Agent=waitingAgent.AgentName, Message=$"Approved {waitingAgent.AgentName} via web" });
            }
            else
            {
                waitingAgent.Status = AgentStatus.Failed; waitingAgent.ErrorMessage="Rejected via web";
                run.Status = PipelineRunStatus.Failed; run.ErrorMessage=$"Approval rejected for {waitingAgent.AgentName}";
                _db.PipelineLogs.Add(new PipelineLog{ PipelineRunId=run.Id, Level="Error", Stage=waitingAgent.AgentName, Message=$"Rejected {waitingAgent.AgentName}" });
            }
            await _db.SaveChangesAsync();
            // On APPROVED, push approved design markdown to Jira custom field (per human review) — field IDs configurable via Pipeline:Jira:ImplementationField / TestDesignField
            if (decision == "APPROVED")
            {
                var jiraField = _jiraOpts.GetFieldForAgent(agentId) ?? FileStateStore.JiraFieldForAgent(agentId);
                if (jiraField != null && sourceArtifact != null)
                {
                    var mdContent = await _store.LoadArtifactAsync(run.JiraId, sourceArtifact);
                    if (!string.IsNullOrWhiteSpace(mdContent))
                    {
                        try
                        {
                            await _jira.UpdateIssueFieldAsync(run.JiraId, jiraField, mdContent);
                            _log.LogInformation("Updated Jira {Jira} field {Field} ({Label}) for {Agent} approval via web", run.JiraId, jiraField, agentId == "technical-design" ? "Implementation" : "Test Design", agentId);
                            _db.PipelineLogs.Add(new PipelineLog{ PipelineRunId=run.Id, PipelineRunAgentId=waitingAgent.Id, Level="Info", Stage=waitingAgent.AgentName, Agent=waitingAgent.AgentName, Message=$"Updated Jira {jiraField} for {agentId} approval" });
                            await _db.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to update Jira {Jira} field {Field} for {Agent} approval via web — approval still recorded", run.JiraId, jiraField, agentId);
                            _db.PipelineLogs.Add(new PipelineLog{ PipelineRunId=run.Id, PipelineRunAgentId=waitingAgent.Id, Level="Warn", Stage=waitingAgent.AgentName, Agent=waitingAgent.AgentName, Message=$"Jira update failed for {jiraField}: {ex.Message}" });
                            await _db.SaveChangesAsync();
                        }
                    }
                }
            }
        }
        return RedirectToPage(new { runId });
    }
}
