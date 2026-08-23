using System.Text.Json;
using LEMUAgenticPipeline.Models;

namespace LEMUAgenticPipeline.State;

/// <summary>
/// Persists per-story pipeline state under .pipeline/state/&lt;JIRA-ID&gt;/ .
/// Mirrors AGENTIC_PIPELINE_IMPLEMENTATION.md §18 Audit Trail.
/// </summary>
public sealed class FileStateStore
{
    private readonly string _root;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public FileStateStore(string root = ".pipeline/state")
    {
        _root = ResolveRoot(root);
    }

    private static string ResolveRoot(string root)
    {
        if (Path.IsPathRooted(root)) return root;
        // Resolve relative to repo root (where DrawVerify.sln / .git is), not to current project dir
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DrawVerify.sln")) && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }
        var baseDir = dir?.FullName ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDir, root));
    }

    public string StoryDir(string storyKey) => Path.Combine(_root, storyKey);
    public string StateFile(string storyKey) => Path.Combine(StoryDir(storyKey), "pipeline-state.json");
    public string LogFile(string storyKey) => Path.Combine(StoryDir(storyKey), "execution-log.json");
    public string JiraFile(string storyKey) => Path.Combine(StoryDir(storyKey), "jira.json");
    public string TechnicalDesignFile(string storyKey) => Path.Combine(StoryDir(storyKey), "technical-design.md");
    public string TestDesignFile(string storyKey) => Path.Combine(StoryDir(storyKey), "test-design.md");
    public string ImplementationResultFile(string storyKey) => Path.Combine(StoryDir(storyKey), "implementation-result.json");
    public string BuildResultFile(string storyKey) => Path.Combine(StoryDir(storyKey), "build-result.json");
    public string TestResultFile(string storyKey) => Path.Combine(StoryDir(storyKey), "test-result.json");
    public string ReviewResultFile(string storyKey) => Path.Combine(StoryDir(storyKey), "review-result.json");

    // ---- Agent-suffixed approval artifacts ----
    // Each human-gated agent gets its own decision file, generated FROM its JIRA-specific source artifact.
    // Example: technical-design-approval.json (FROM technical-design.md), test-design-approval.json (FROM test-design.md)
    // No separate approval-request.md is generated — the agent's own <agent>.md is the full content to be validated.
    // ApprovalRequestFile() is retained for backwards compat / manual inspection but not used by orchestrator/runner.
    public string ApprovalRequestFile(string storyKey, string agentId) => Path.Combine(StoryDir(storyKey), $"{SanitizeAgentId(agentId)}-approval-request.md");
    public string ApprovalFile(string storyKey, string agentId) => Path.Combine(StoryDir(storyKey), $"{SanitizeAgentId(agentId)}-approval.json");

    /// <summary>Resolve agent id for a PipelineState gate (used by orchestrator CLI).</summary>
    public static string AgentIdForState(PipelineState state) => state switch
    {
        PipelineState.TechDesignApproval => "technical-design",
        PipelineState.TestDesignApproval => "test-design",
        PipelineState.HumanReview => "human-review",
        PipelineState.TechDesignReady => "technical-design",
        PipelineState.TestDesignReady => "test-design",
        _ => SanitizeAgentId(state.ToString().ToLowerInvariant().Replace("_", "-"))
    };

    /// <summary>Source artifact (per-JIRA file) that an approval request is generated from.</summary>
    public static string? SourceArtifactForAgent(string agentId) => SanitizeAgentId(agentId) switch
    {
        "technical-design" => "technical-design.md",
        "test-design" => "test-design.md",
        "human-review" => "review-result.json",
        "code-review" => "review-result.json",
        _ => null
    };

    /// <summary>Primary artifact file produced by an agent (per-JIRA). Used for UI viewing.</summary>
    public static string? ArtifactFileForAgent(string agentId) => SanitizeAgentId(agentId) switch
    {
        "technical-design" => "technical-design.md",
        "test-design" => "test-design.md",
        "implementation" => "implementation-result.json",
        "test" => "test-result.json",
        "code-review" => "review-result.json",
        "human-review" => "review-result.json",
        "verifier" => "verification-result.md",
        _ => null
    };

    /// <summary>Jira custom field for approved design sync (per human review).</summary>
    public static string? JiraFieldForAgent(string agentId) => SanitizeAgentId(agentId) switch
    {
        "technical-design" => "customfield_10168", // Implementation
        "test-design" => "customfield_10201", // Test Design
        _ => null
    };

    public static string? JiraFieldLabelForAgent(string agentId) => SanitizeAgentId(agentId) switch
    {
        "technical-design" => "Implementation",
        "test-design" => "Test Design",
        _ => null
    };

    public static string? SourceArtifactForState(PipelineState state) => SourceArtifactForAgent(AgentIdForState(state));

    public static string SanitizeAgentId(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return "generic";
        // allow only safe chars for filename, mimic Path.GetFileName sanitization
        var sanitized = agentId.Trim().ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(c.ToString(), "-");
        return sanitized;
    }

    public async Task EnsureStoryDirAsync(string storyKey)
    {
        Directory.CreateDirectory(StoryDir(storyKey));
        await Task.CompletedTask;
    }

    public async Task SaveStateAsync(string storyKey, PipelineStateData data)
    {
        await EnsureStoryDirAsync(storyKey);
        var json = JsonSerializer.Serialize(data, JsonOpts);
        await File.WriteAllTextAsync(StateFile(storyKey), json);
    }

    public async Task<PipelineStateData?> LoadStateAsync(string storyKey)
    {
        var path = StateFile(storyKey);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<PipelineStateData>(json);
    }

    public async Task AppendLogAsync(string storyKey, ExecutionLogEntry entry)
    {
        await EnsureStoryDirAsync(storyKey);
        List<ExecutionLogEntry> log = [];
        var path = LogFile(storyKey);
        if (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path);
            log = JsonSerializer.Deserialize<List<ExecutionLogEntry>>(existing) ?? [];
        }
        log.Add(entry);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(log, JsonOpts));
    }

    public async Task SaveArtifactAsync(string storyKey, string fileName, string content)
    {
        await EnsureStoryDirAsync(storyKey);
        await File.WriteAllTextAsync(Path.Combine(StoryDir(storyKey), fileName), content);
    }

    public async Task<string?> LoadArtifactAsync(string storyKey, string fileName)
    {
        var path = Path.Combine(StoryDir(storyKey), fileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    /// <summary>Check if an agent's primary artifact exists for a story.</summary>
    public bool ArtifactExists(string storyKey, string agentId)
    {
        var file = ArtifactFileForAgent(agentId);
        if (file == null) return false;
        return File.Exists(Path.Combine(StoryDir(storyKey), file));
    }

    /// <summary>Safe path for an agent artifact (null if unknown).</summary>
    public string? ArtifactPath(string storyKey, string agentId)
    {
        var file = ArtifactFileForAgent(agentId);
        return file == null ? null : Path.Combine(StoryDir(storyKey), file);
    }
}
