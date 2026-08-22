using LEMUAgenticPipeline.Web.Data;
using LEMUAgenticPipeline.Web.Hubs;
using LEMUAgenticPipeline.Web.Services;
using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Jira;
using LEMUAgenticPipeline.OpenAI;
using LEMUAgenticPipeline.OpenCode;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Also load pipeline orchestrator config (single source for Jira, ModelRouting, etc.)
builder.Configuration.AddJsonFile(".pipeline/LEMUAgenticPipeline.Orchestrator/appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile(".pipeline/LEMUAgenticPipeline/appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables(prefix: "Pipeline_");

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.Configure<AgentRegistryOptions>(builder.Configuration.GetSection("Pipeline:Agents"));
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection("Pipeline"));
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<FileStateStore>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PipelineOptions>>().Value;
    return new FileStateStore(opts.Paths.StateRoot);
});
// Pipeline core (deterministic) — web is control plane, orchestrator owns execution
builder.Services.AddSingleton<IJiraService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PipelineOptions>>();
    var log = sp.GetRequiredService<ILogger<JiraRestService>>();
    var http = new HttpClient();
    var jiraOpts = opts.Value.Jira;
    if (!string.IsNullOrWhiteSpace(jiraOpts.BaseUrl))
        http.BaseAddress = new Uri(jiraOpts.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(jiraOpts.TimeoutSeconds);
    return new JiraRestService(http, opts, log);
});
RegisterCodeAgentService(builder.Services, builder.Configuration);
// Stub Git/Build for web host (PipelineOrchestrator requires them for DI, even if web's PipelineRunner uses direct ICodeAgentService)
builder.Services.AddSingleton<IGitService, CliGitService>();
builder.Services.AddSingleton<IBuildTestService, DotNetBuildTestService>();
builder.Services.AddSingleton<LEMUAgenticPipeline.Orchestrator.PipelineOrchestrator>();
builder.Services.AddScoped<PipelineScheduler>();
builder.Services.AddScoped<PipelineRunner>();
builder.Services.AddHostedService<PipelineWorker>();
builder.Services.AddDbContext<PipelineDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Pipeline") ?? "Data Source=pipeline.db"));

var app = builder.Build();

// Ensure DB created (MVP: EnsureCreated, not migrations for brevity)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();
app.MapHub<PipelineHub>("/hubs/pipeline");
app.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTime.UtcNow }));
app.Run();

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

// Stubs for PipelineOrchestrator DI (web is control plane, Git/Build are orchestrator-owned; stubs suffice for DI)
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
