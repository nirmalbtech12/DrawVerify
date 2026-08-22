using System.ComponentModel.DataAnnotations;

namespace LEMUAgenticPipeline.Web.Models;

public enum ScheduleType { Immediate, OneTime }
public enum PipelineRunStatus { Queued, Running, Completed, Failed, WaitingForApproval, Cancelled }
public enum AgentStatus { NotSelected, Queued, Running, Completed, Failed, Skipped, WaitingForApproval }

public sealed class PipelineRun
{
    public int Id { get; set; }
    [MaxLength(32)] public string RunId { get; set; } = string.Empty; // RUN-20260821-001
    [MaxLength(32)] public string JiraId { get; set; } = string.Empty; // KAN-19
    [MaxLength(32)] public string IssueType { get; set; } = "Story";
    public ScheduleType ScheduleType { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Queued;
    [MaxLength(64)] public string? CurrentStage { get; set; }
    [MaxLength(64)] public string? CurrentAgent { get; set; }
    [MaxLength(128)] public string? BranchName { get; set; }
    [MaxLength(64)] public string? CommitHash { get; set; }
    public int? PullRequestNumber { get; set; }
    [MaxLength(512)] public string? PullRequestUrl { get; set; }
    [MaxLength(2048)] public string? ErrorMessage { get; set; }
    public string? SelectedAgentsJson { get; set; } // persisted list of AgentIds for audit
    public bool StopOnFailure { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(64)] public string CreatedBy { get; set; } = "system";
    public List<PipelineRunAgent> Agents { get; set; } = [];
    public List<PipelineLog> Logs { get; set; } = [];
    public double? DurationSeconds => StartedAt.HasValue && CompletedAt.HasValue ? (CompletedAt - StartedAt)?.TotalSeconds : null;
}

public sealed class PipelineRunAgent
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public PipelineRun PipelineRun { get; set; } = null!;
    [MaxLength(64)] public string AgentId { get; set; } = string.Empty; // technical-design
    [MaxLength(64)] public string AgentName { get; set; } = string.Empty;
    public int ExecutionOrder { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Queued;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? DurationSeconds { get; set; }
    [MaxLength(2048)] public string? Result { get; set; }
    [MaxLength(2048)] public string? ErrorMessage { get; set; }
}

public sealed class PipelineLog
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public PipelineRun PipelineRun { get; set; } = null!;
    public int? PipelineRunAgentId { get; set; }
    public PipelineRunAgent? PipelineRunAgent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [MaxLength(16)] public string Level { get; set; } = "Info"; // Info/Warn/Error
    [MaxLength(64)] public string? Stage { get; set; }
    [MaxLength(64)] public string? Agent { get; set; }
    [MaxLength(4096)] public string Message { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

// For scheduling queue when multiple stories
public sealed class PipelineSchedule
{
    public int Id { get; set; }
    [MaxLength(32)] public string ScheduleId { get; set; } = string.Empty; // SCH-20260821-001
    public ScheduleType ScheduleType { get; set; }
    public DateTime ScheduledAt { get; set; }
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Queued;
    public bool StopOnFailure { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<PipelineScheduleItem> Items { get; set; } = [];
}

public sealed class PipelineScheduleItem
{
    public int Id { get; set; }
    public int PipelineScheduleId { get; set; }
    public PipelineSchedule PipelineSchedule { get; set; } = null!;
    [MaxLength(32)] public string JiraId { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public int? PipelineRunId { get; set; }
    public PipelineRun? PipelineRun { get; set; }
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Queued;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    [MaxLength(2048)] public string? ErrorMessage { get; set; }
}
