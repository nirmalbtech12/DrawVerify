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
}
