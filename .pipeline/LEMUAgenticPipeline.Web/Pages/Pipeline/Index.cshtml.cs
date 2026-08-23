using LEMUAgenticPipeline.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using LEMUAgenticPipeline.Web.Models;

public class PipelineIndexModel : PageModel
{
    private readonly PipelineDbContext _db;
    public PipelineIndexModel(PipelineDbContext db) => _db = db;
    public int Total, Running, Queued, Completed, Failed, Waiting;
    public List<PipelineRun> Recent { get; set; } = [];
    public async Task OnGetAsync()
    {
        Total = await _db.PipelineRuns.CountAsync();
        Running = await _db.PipelineRuns.CountAsync(r => r.Status == PipelineRunStatus.Running);
        Queued = await _db.PipelineRuns.CountAsync(r => r.Status == PipelineRunStatus.Queued);
        Completed = await _db.PipelineRuns.CountAsync(r => r.Status == PipelineRunStatus.Completed);
        Failed = await _db.PipelineRuns.CountAsync(r => r.Status == PipelineRunStatus.Failed);
        Waiting = await _db.PipelineRuns.CountAsync(r => r.Status == PipelineRunStatus.WaitingForApproval);
        Recent = await _db.PipelineRuns.OrderByDescending(r => r.CreatedAt).Take(10).ToListAsync();
    }
}
