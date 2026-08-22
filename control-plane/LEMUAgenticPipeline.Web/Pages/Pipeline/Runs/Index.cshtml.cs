using LEMUAgenticPipeline.Web.Data;
using LEMUAgenticPipeline.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

public class RunsIndexModel : PageModel
{
    private readonly PipelineDbContext _db;
    public RunsIndexModel(PipelineDbContext db) => _db = db;
    public string? Q { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Status { get; set; }
    public int CurrentPage { get; set; } = 1;
    public List<PipelineRun> Runs { get; set; } = [];
    public async Task OnGetAsync(string? q, DateTime? from, DateTime? to, string? status, int page=1)
    {
        Q=q; From=from; To=to; Status=status; CurrentPage=page;
        var qry = _db.PipelineRuns.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) qry = qry.Where(r => r.JiraId.Contains(q) || r.RunId.Contains(q));
        if (from.HasValue) qry = qry.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue) qry = qry.Where(r => r.CreatedAt <= to.Value.AddDays(1));
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PipelineRunStatus>(status, out var st)) qry = qry.Where(r => r.Status == st);
        Runs = await qry.OrderByDescending(r => r.CreatedAt).Skip((page-1)*20).Take(20).ToListAsync();
    }
}
