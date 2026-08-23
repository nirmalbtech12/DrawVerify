using LEMUAgenticPipeline.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LEMUAgenticPipeline.Web.Data;

public sealed class PipelineDbContext : DbContext
{
    public PipelineDbContext(DbContextOptions<PipelineDbContext> options) : base(options) { }

    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<PipelineRunAgent> PipelineRunAgents => Set<PipelineRunAgent>();
    public DbSet<PipelineLog> PipelineLogs => Set<PipelineLog>();
    public DbSet<PipelineSchedule> PipelineSchedules => Set<PipelineSchedule>();
    public DbSet<PipelineScheduleItem> PipelineScheduleItems => Set<PipelineScheduleItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PipelineRun>(e =>
        {
            e.HasIndex(x => x.RunId).IsUnique();
            e.HasIndex(x => x.JiraId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.HasMany(x => x.Agents).WithOne(x => x.PipelineRun).HasForeignKey(x => x.PipelineRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Logs).WithOne(x => x.PipelineRun).HasForeignKey(x => x.PipelineRunId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PipelineRunAgent>(e =>
        {
            e.HasIndex(x => new { x.PipelineRunId, x.AgentId }).IsUnique();
            e.HasMany<PipelineLog>().WithOne(x => x.PipelineRunAgent).HasForeignKey(x => x.PipelineRunAgentId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<PipelineLog>(e => { e.HasIndex(x => x.PipelineRunId); e.HasIndex(x => x.Timestamp); });
        b.Entity<PipelineSchedule>(e =>
        {
            e.HasIndex(x => x.ScheduleId).IsUnique();
            e.HasMany(x => x.Items).WithOne(x => x.PipelineSchedule).HasForeignKey(x => x.PipelineScheduleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PipelineScheduleItem>(e => e.HasIndex(x => x.JiraId));
    }
}
