using LEMUAgenticPipeline.Web.Data;
using LEMUAgenticPipeline.Web.Models;
using LEMUAgenticPipeline.Services;
using Microsoft.EntityFrameworkCore;

namespace LEMUAgenticPipeline.Web.Services;

public sealed class CreatePipelineRequest
{
    public string JiraIdsRaw { get; set; } = string.Empty; // "KAN-19,KAN-20"
    public ScheduleType ScheduleType { get; set; }
    public DateTime? OneTimeAt { get; set; } // today HH:mm
    public List<string> SelectedAgentIds { get; set; } = [];
    public bool StopOnFailure { get; set; } = true;
}

public sealed class PipelineScheduler
{
    private readonly PipelineDbContext _db;
    private readonly AgentRegistry _registry;
    private readonly ILogger<PipelineScheduler> _log;
    private readonly IJiraService? _jira;
    public PipelineScheduler(PipelineDbContext db, AgentRegistry registry, ILogger<PipelineScheduler> log, IServiceProvider sp)
    { _db = db; _registry = registry; _log = log; _jira = sp.GetService<IJiraService>(); }

    public (bool Valid, List<string> Errors) Validate(CreatePipelineRequest req) => ValidateAsync(req).GetAwaiter().GetResult();

    public async Task<(bool Valid, List<string> Errors)> ValidateAsync(CreatePipelineRequest req)
    {
        var errors = new List<string>();
        var jiraIds = ParseJiraIds(req.JiraIdsRaw);
        if (jiraIds.Count == 0) errors.Add("At least one Jira ID required (e.g. KAN-19)");
        if (jiraIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != jiraIds.Count) errors.Add("Duplicate Jira IDs");
        foreach (var id in jiraIds)
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Z][A-Z0-9]+-\d+$"))
                errors.Add($"Invalid Jira ID: {id}");

        if (req.ScheduleType == ScheduleType.OneTime)
        {
            if (req.OneTimeAt == null) errors.Add("OneTime requires time");
            else
            {
                // OneTimeAt is built as DateTime.Today + TimeSpan, Kind=Unspecified → treat as local
                var t = DateTime.SpecifyKind(req.OneTimeAt.Value, DateTimeKind.Local);
                if (t.Date != DateTime.Today) errors.Add("OneTime only allows today");
                if (t <= DateTime.Now) errors.Add("OneTime time must be in the future");
            }
        }

        var (valid, depErrors, _) = _registry.ValidateSelection(req.SelectedAgentIds);
        errors.AddRange(depErrors);

        // Check already running for same Jira
        foreach (var id in jiraIds)
        {
            var running = _db.PipelineRuns.Any(r => r.JiraId == id && r.Status == PipelineRunStatus.Running);
            if (running) errors.Add($"Pipeline already running for {id}");
        }

        // Jira existence (orchestrator-owned, best-effort — don't block if Jira not configured)
        if (_jira != null && jiraIds.Count > 0)
        {
            foreach (var id in jiraIds)
            {
                try
                {
                    // Use GetStoryAsync — will throw JiraServiceException 404 if not found
                    await _jira.GetStoryAsync(id);
                }
                catch (LEMUAgenticPipeline.Configuration.JiraServiceException ex) when (ex.StatusCode == 404)
                {
                    errors.Add($"Jira issue not found: {id}");
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Jira validation skipped for {Jira} (service unavailable)", id);
                    // Don't add error if Jira is unavailable (e.g., no token) — allow queueing, worker will fail with clear log
                }
            }
        }

        return (errors.Count == 0, errors);
    }

    public static List<string> ParseJiraIds(string raw)
        => raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Select(s => s.ToUpperInvariant().Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

    public async Task<PipelineSchedule> CreateScheduleAsync(CreatePipelineRequest req, string createdBy = "web")
    {
        var jiraIds = ParseJiraIds(req.JiraIdsRaw);
        var (valid, errors) = await ValidateAsync(req);
        if (!valid) throw new InvalidOperationException(string.Join("; ", errors));

        // Transaction for atomic schedule creation (fixes concurrency gap §5)
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Normalize selection with auto-selected deps — ensure distinct (fixes UNIQUE constraint on PipelineRunAgents)
        var (ok, _, auto) = _registry.ValidateSelection(req.SelectedAgentIds);
        var finalSelected = req.SelectedAgentIds.Concat(auto).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ordered = _registry.OrderedForExecution(finalSelected)
            .GroupBy(a => a.AgentId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        var scheduleId = $"SCH-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Random.Shared.Next(100, 999)}";
        var scheduledAt = req.ScheduleType == ScheduleType.Immediate ? DateTime.UtcNow : DateTime.SpecifyKind(req.OneTimeAt!.Value, DateTimeKind.Local).ToUniversalTime();
        var status = PipelineRunStatus.Queued;

        var schedule = new PipelineSchedule
        {
            ScheduleId = scheduleId,
            ScheduleType = req.ScheduleType,
            ScheduledAt = scheduledAt,
            Status = status,
            StopOnFailure = req.StopOnFailure,
        };
        _db.PipelineSchedules.Add(schedule);
        await _db.SaveChangesAsync();

        int seq = 1;
        foreach (var jiraId in jiraIds)
        {
            var runId = $"RUN-{DateTime.UtcNow:yyyyMMdd}-{schedule.Id:D4}-{seq:D2}";
            var run = new PipelineRun
            {
                RunId = runId,
                JiraId = jiraId,
                ScheduleType = req.ScheduleType,
                ScheduledAt = scheduledAt,
                Status = PipelineRunStatus.Queued,
                CreatedBy = createdBy,
                SelectedAgentsJson = System.Text.Json.JsonSerializer.Serialize(finalSelected),
                StopOnFailure = req.StopOnFailure,
            };
            _db.PipelineRuns.Add(run);
            await _db.SaveChangesAsync();

            // Persist PipelineRunAgents (audit selected) — use HashSet to avoid duplicate in same batch
            var seenAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ag in ordered)
            {
                if (!seenAgents.Add(ag.AgentId)) continue; // skip duplicate AgentId for same run
                _db.PipelineRunAgents.Add(new PipelineRunAgent
                {
                    PipelineRunId = run.Id,
                    AgentId = ag.AgentId,
                    AgentName = ag.Name,
                    ExecutionOrder = ag.ExecutionOrder,
                    Status = AgentStatus.Queued
                });
            }

            _db.PipelineScheduleItems.Add(new PipelineScheduleItem
            {
                PipelineScheduleId = schedule.Id,
                JiraId = jiraId,
                SequenceNumber = seq++,
                PipelineRunId = run.Id,
                Status = PipelineRunStatus.Queued
            });

            // Initial log
            _db.PipelineLogs.Add(new PipelineLog
            {
                PipelineRunId = run.Id,
                Level = "Info",
                Stage = "Queued",
                Message = $"Created run {runId} for {jiraId} schedule {req.ScheduleType} agents: {string.Join(",", finalSelected)}"
            });
        }
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE constraint failed") == true)
        {
            await tx.RollbackAsync();
            _log.LogError(ex, "Unique constraint failed for schedule {ScheduleId} — likely duplicate AgentId for same PipelineRunId. FinalSelected: {Agents}, Ordered: {Ordered}", scheduleId, string.Join(",", finalSelected), string.Join(",", ordered.Select(a => a.AgentId)));
            throw new InvalidOperationException($"Duplicate agent for same run (likely duplicate AgentId in selection). Agents: {string.Join(",", finalSelected)} — please retry with distinct agents.", ex);
        }
        _log.LogInformation("Created schedule {ScheduleId} with {Count} runs", scheduleId, jiraIds.Count);
        return schedule;
    }
}
