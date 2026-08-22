using LEMUAgenticPipeline.Web.Data;
using LEMUAgenticPipeline.Web.Models;
using LEMUAgenticPipeline.Web.Services;
using LEMUAgenticPipeline.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

public class DetailModel : PageModel
{
    private readonly PipelineDbContext _db;
    private readonly AgentRegistry _registry;
    private readonly FileStateStore _store;
    public DetailModel(PipelineDbContext db, AgentRegistry registry, FileStateStore store){ _db=db; _registry=registry; _store=store; }
    public PipelineRun? Run { get; set; }
    public List<PipelineLog> Logs { get; set; } = [];
    public List<string> NotSelected { get; set; } = [];
    public async Task OnGetAsync(string runId)
    {
        Run = await _db.PipelineRuns.Include(r=>r.Agents).FirstOrDefaultAsync(r=> r.RunId==runId);
        if(Run==null) return;
        Logs = await _db.PipelineLogs.Where(l=>l.PipelineRunId==Run.Id).OrderBy(l=>l.Timestamp).ToListAsync();
        var selected = Run.Agents.Select(a=>a.AgentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        NotSelected = _registry.All.Where(a=> !selected.Contains(a.AgentId)).Select(a=>a.Name).ToList();
    }

    public async Task<IActionResult> OnPostApproveAsync(string runId, string decision)
    {
        var run = await _db.PipelineRuns.Include(r=>r.Agents).FirstOrDefaultAsync(r=> r.RunId==runId);
        if(run==null) return NotFound();
        decision = (decision ?? "APPROVED").ToUpperInvariant();
        // Write approval.json for orchestrator (FileStateStore) — human gate
        await _store.SaveArtifactAsync(run.JiraId, "approval.json", System.Text.Json.JsonSerializer.Serialize(new { decision, runId, at = DateTime.UtcNow }));
        // Also update DB for immediate UI feedback
        var waitingAgent = run.Agents.FirstOrDefault(a=> a.Status==AgentStatus.WaitingForApproval);
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
        }
        return RedirectToPage(new { runId });
    }
}
