using LEMUAgenticPipeline.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace LEMUAgenticPipeline.Web.Services;

public sealed class PipelineWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<PipelineWorker> _log;
    public PipelineWorker(IServiceProvider sp, ILogger<PipelineWorker> log) { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("PipelineWorker started");
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "Worker tick failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
        _log.LogInformation("PipelineWorker stopped");
    }

    private static readonly SemaphoreSlim _gitLock = new(1, 1); // §22 Git workspace lock

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<PipelineRunner>();

        // Transaction + sequential guard (fixes concurrency gap §5)
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var item = await db.PipelineScheduleItems
            .Include(i => i.PipelineSchedule)
            .Include(i => i.PipelineRun)
            .Where(i => i.Status == Models.PipelineRunStatus.Queued && i.PipelineSchedule.ScheduledAt <= now)
            .OrderBy(i => i.PipelineSchedule.ScheduledAt).ThenBy(i => i.SequenceNumber)
            .FirstOrDefaultAsync(ct);

        if (item == null) { await tx.RollbackAsync(ct); return; }

        var anyRunning = await db.PipelineScheduleItems.AnyAsync(i => i.Status == Models.PipelineRunStatus.Running, ct);
        if (anyRunning) { await tx.RollbackAsync(ct); return; }

        // Mark as Running atomically before releasing transaction
        item.Status = Models.PipelineRunStatus.Running;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogInformation("Dequeued {RunId} for {Jira}", item.PipelineRun?.RunId, item.JiraId);
        await _gitLock.WaitAsync(ct);
        try { await runner.RunItemAsync(item, ct); }
        finally { _gitLock.Release(); }
    }
}
