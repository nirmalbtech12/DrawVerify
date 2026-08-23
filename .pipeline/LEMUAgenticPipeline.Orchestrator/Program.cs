using System.CommandLine;
using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Examples;
using LEMUAgenticPipeline.OpenAI;
using LEMUAgenticPipeline.OpenCode;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($".pipeline/LEMUAgenticPipeline.Orchestrator/appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "Pipeline_"); // Pipeline__ModelRouting__TechnicalDesigner=...

builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection("Pipeline"));
builder.Services.AddSingleton<FileStateStore>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PipelineOptions>>().Value;
    return new FileStateStore(opts.Paths.StateRoot);
});

// Jira is orchestrator-only: agents receive StoryContext, never IJiraService.
// HttpClient is owned by JiraRestService (no AddHttpClient dependency).
builder.Services.AddSingleton<IJiraService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PipelineOptions>>();
    var log = sp.GetRequiredService<ILogger<LEMUAgenticPipeline.Jira.JiraRestService>>();
    var http = new HttpClient();
    var jiraOpts = opts.Value.Jira;
    if (!string.IsNullOrWhiteSpace(jiraOpts.BaseUrl))
        http.BaseAddress = new Uri(jiraOpts.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(jiraOpts.TimeoutSeconds);
    return new LEMUAgenticPipeline.Jira.JiraRestService(http, opts, log);
});
builder.Services.AddSingleton<IGitService, CliGitService>();
builder.Services.AddSingleton<IBuildTestService, DotNetBuildTestService>();
RegisterCodeAgentService(builder.Services, builder.Configuration);
builder.Services.AddSingleton<LEMUAgenticPipeline.Orchestrator.PipelineOrchestrator>();

var host = builder.Build();

// CLI: pipeline run <JIRA-ID> | pipeline approve <JIRA-ID> --decision APPROVED
var root = new RootCommand("LEMU Agentic Pipeline Orchestrator");

var runCmd = new Command("run", "Run pipeline for a story");
var storyArg = new Argument<string>("story", "Jira key e.g. DV-TEST-001");
runCmd.AddArgument(storyArg);
runCmd.SetHandler(async (string story) =>
{
    try
    {
        using var scope = host.Services.CreateScope();
        var jira = scope.ServiceProvider.GetRequiredService<IJiraService>();
        var git = scope.ServiceProvider.GetRequiredService<IGitService>();
        var build = scope.ServiceProvider.GetRequiredService<IBuildTestService>();
        var agents = scope.ServiceProvider.GetRequiredService<ICodeAgentService>();
        var store = scope.ServiceProvider.GetRequiredService<FileStateStore>();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<PipelineOptions>>();
        var lf = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Starting pipeline for {Story}", story);
        await StoryFlows.RunUntilTechDesignApprovalAsync(story, jira, git, agents, store, opts, lf);
    }
    catch (LEMUAgenticPipeline.Configuration.JiraServiceException ex)
    {
        Console.Error.WriteLine($"Jira error for {ex.IssueKey ?? story}: {ex.Message} (HTTP {(ex.StatusCode?.ToString() ?? "n/a")})");
        if (ex.StatusCode == 404)
            Console.Error.WriteLine($"Hint: Issue {story} not found via REST. Via MCP, KAN-19 exists (US-1.1.1: Upload STEP file) — REST failed likely due to missing/invalid JIRA_TOKEN or BaseUrl. Set: export JIRA_TOKEN=<PAT> && export Pipeline__Jira__BaseUrl=https://lemutechnologies.atlassian.net");
        if (ex.StatusCode is 401 or 403)
            Console.Error.WriteLine($"Hint: Jira auth failed. Ensure Pipeline:Jira:BaseUrl and env {ex.Message} (JIRA_TOKEN / email) are correct. For Bearer use empty Email, for Basic set Email.");
        Environment.ExitCode = 2;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Pipeline blocked: {ex.Message}");
        Environment.ExitCode = 3;
    }
}, storyArg);

var approveCmd = new Command("approve", "Approve a human gate");
var approveStory = new Argument<string>("story", "Jira key");
var decisionOpt = new Option<string>("--decision", () => "APPROVED", "APPROVED | REJECTED | REWORK");
var stateOpt = new Option<string>("--state", "PipelineState to approve");
approveCmd.AddArgument(approveStory);
approveCmd.AddOption(decisionOpt);
approveCmd.AddOption(stateOpt);
approveCmd.SetHandler(async (string story, string decision, string state) =>
{
    var store = host.Services.GetRequiredService<FileStateStore>();
    // Map PipelineState -> agentId so approval is per-agent and bound to its JIRA-specific source artifact
    string agentId;
    string? sourceArtifact = null;
    if (!string.IsNullOrWhiteSpace(state) && Enum.TryParse<LEMUAgenticPipeline.Models.PipelineState>(state, ignoreCase: true, out var parsed))
    {
        agentId = FileStateStore.AgentIdForState(parsed);
        sourceArtifact = FileStateStore.SourceArtifactForState(parsed);
    }
    else if (!string.IsNullOrWhiteSpace(state))
    {
        agentId = FileStateStore.SanitizeAgentId(state);
        sourceArtifact = FileStateStore.SourceArtifactForAgent(agentId);
    }
    else
    {
        agentId = "generic";
    }
    // Generate approval JSON FROM the specific JIRA file (include source hash for traceability)
    string? sourceHash = null;
    if (sourceArtifact != null)
    {
        var src = await store.LoadArtifactAsync(story, sourceArtifact);
        if (src != null)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            sourceHash = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(src))).ToLowerInvariant()[..16];
        }
    }
    var payload = new { decision, state, agent = agentId, sourceArtifact, sourceHash, at = DateTime.UtcNow };
    var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    var fileName = Path.GetFileName(store.ApprovalFile(story, agentId));
    await store.SaveArtifactAsync(story, fileName, json);
    Console.WriteLine($"Recorded {decision} for {story} at {state} (agent: {agentId}, source: {sourceArtifact ?? "none"}) -> {fileName}");
    // On APPROVED, sync approved design markdown to Jira custom field (per human review) — field IDs configurable via Pipeline:Jira:ImplementationField / TestDesignField
    if (string.Equals(decision, "APPROVED", StringComparison.OrdinalIgnoreCase))
    {
        var jiraOpts = host.Services.GetRequiredService<IOptions<PipelineOptions>>().Value.Jira;
        var jiraField = jiraOpts.GetFieldForAgent(agentId) ?? FileStateStore.JiraFieldForAgent(agentId);
        if (jiraField != null && sourceArtifact != null)
        {
            var mdContent = await store.LoadArtifactAsync(story, sourceArtifact);
            if (!string.IsNullOrWhiteSpace(mdContent))
            {
                try
                {
                    var jira = host.Services.GetRequiredService<IJiraService>();
                    await jira.UpdateIssueFieldAsync(story, jiraField, mdContent);
                    Console.WriteLine($"Updated Jira {story} field {jiraField} ({FileStateStore.JiraFieldLabelForAgent(agentId)}) for {agentId} approval");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to update Jira {story} field {jiraField} for {agentId}: {ex.Message}");
                }
            }
        }
    }
}, approveStory, decisionOpt, stateOpt);

root.AddCommand(runCmd);
root.AddCommand(approveCmd);
return await root.InvokeAsync(args);

static void RegisterCodeAgentService(IServiceCollection services, IConfiguration configuration)
{
    var provider = configuration.GetValue<string>("Pipeline:Provider") ?? "OpenCode";
    switch (provider.Trim().ToUpperInvariant())
    {
        case "OPENCODE":
            services.AddSingleton<ICodeAgentService, OpenCodeAgentService>();
            break;
        case "OPENAI":
            services.AddSingleton<HttpClient>();
            services.AddSingleton<ICodeAgentService, OpenAIAgentService>();
            break;
        default:
            throw new InvalidOperationException(
                $"Unsupported pipeline provider '{provider}'. Configure Pipeline:Provider as OpenCode or OpenAI.");
    }
}

// ---- minimal stubs so `dotnet build` passes; replace with real implementations (Git/Build remain stubbed) ----

sealed class CliGitService : IGitService
{
    public Task AddFilesAsync(params string[] p) => Task.CompletedTask;
    public Task CommitAsync(string m) => Task.CompletedTask;
    public Task CreateBranchAsync(string b) => Task.CompletedTask;
    public Task CreatePrAsync(string t, string b) => Task.CompletedTask;
    public Task<string> GetCurrentBranchAsync() => Task.FromResult("main");
    public Task<string> GetDiffAsync() => Task.FromResult(string.Empty);
    public Task<bool> IsWorkingTreeCleanAsync() => Task.FromResult(true);
    public Task<bool> PrExistsAsync(string t) => Task.FromResult(false);
    public Task PullAsync() => Task.CompletedTask;
    public Task PushAsync() => Task.CompletedTask;
    public Task SwitchBranchAsync(string b) => Task.CompletedTask;
}

sealed class DotNetBuildTestService : IBuildTestService
{
    public async Task<BuildResult> BuildAsync(string s, string c = "Debug")
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"build {s} -c {c}") { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new BuildResult { Success = p.ExitCode == 0, Output = o };
    }
    public async Task<TestResult> TestAsync(string s, string c = "Debug")
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"test {s} -c {c}") { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new TestResult { Success = p.ExitCode == 0, Output = o };
    }
    public bool IsStoryRelatedFailure(string o) => true;
}
