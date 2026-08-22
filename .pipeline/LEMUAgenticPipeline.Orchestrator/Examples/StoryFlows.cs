using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Models;
using LEMUAgenticPipeline.Services;
using LEMUAgenticPipeline.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LEMUAgenticPipeline.Examples;

/// <summary>
/// Example orchestration flows — copy into Program.cs or a hosted service.
/// Demonstrates safety limits: human gates STOP the flow; no auto-merge; max 2 recoveries.
/// </summary>
public static class StoryFlows
{
    /// <summary>
    /// Flow 1: DISCOVERED -> TECH_DESIGN_APPROVAL (stops for human).
    /// Includes Jira retrieval, hierarchy validation, agent invocation, and gate.
    /// </summary>
    public static async Task RunUntilTechDesignApprovalAsync(
        string storyKey,
        IJiraService jira,
        IGitService git,
        ICodeAgentService agents,
        FileStateStore store,
        IOptions<PipelineOptions> opts,
        ILoggerFactory lf)
    {
        var orch = new Orchestrator.PipelineOrchestrator(jira, git,
            new StubBuildTestService(), agents, store, opts, lf.CreateLogger<Orchestrator.PipelineOrchestrator>());

        // 1. DISCOVERED
        var ctx = await orch.StartStoryAsync(storyKey);
        await orch.TransitionStateAsync(PipelineState.Validated);

        // Validate hierarchy (Epic -> Feature -> Story). Epic is max root, so Story->Epic is valid.
        if (!await jira.ValidateHierarchyAsync(storyKey))
        {
            await orch.TransitionStateAsync(PipelineState.Blocked);
            Console.Error.WriteLine($"[BLOCKED] Hierarchy validation failed for {storyKey} — see execution-log.json. Epic is max root; ensure Story has parent Feature/Epic.");
            return;
        }

        // 2. TECH_DESIGN_REQUIRED -> READY (agent)
        await orch.TransitionStateAsync(PipelineState.TechDesignRequired);

        // Guard: ensure clean working tree / main sync before agent work (orchestrator responsibility)
        if (!await git.IsWorkingTreeCleanAsync())
            throw new InvalidOperationException("Working tree not clean — aborting");

        var techPrompt = Path.Combine(opts.Value.Paths.PromptDir, "technical-design.md");
        var agentResult = await agents.InvokeAgentAsync(AgentType.TechnicalDesigner, ctx, techPrompt);
        if (!agentResult.IsValid)
            throw new InvalidOperationException($"Technical design failed schema validation: {agentResult.OutputPath}");

        ctx.TechnicalDesignContent = agentResult.Output;
        ctx.TechnicalDesignPath = agentResult.OutputPath;

        await orch.TransitionStateAsync(PipelineState.TechDesignReady);
        await orch.TransitionStateAsync(PipelineState.TechDesignApproval);

        // 3. STOP — human gate. Orchestrator blocks here; do NOT auto-advance.
        Console.WriteLine($"[GATE] Technical design ready for {storyKey}: {agentResult.OutputPath}");
        Console.WriteLine($"       Awaiting human approval. Run: pipeline approve {storyKey} --state TechDesignApproval --decision APPROVED");
        // In CLI: poll for approval.json; in service: return and resume on webhook
        // await orch.RequestHumanApprovalAsync("Review technical-design.md", out var decision);
        // if (decision == "APPROVED") await orch.TransitionStateAsync(PipelineState.TestDesignRequired);
        // else                         await orch.TransitionStateAsync(PipelineState.TechDesignRequired); // rework
    }

    /// <summary>
    /// Flow 2: TEST_DESIGN_APPROVAL -> ... -> PR_READY (assumes designs approved).
    /// Enforces: branch created by orchestrator, no auto-repair, no auto-merge.
    /// </summary>
    public static async Task RunFromTestDesignApprovalToPrReadyAsync(
        string storyKey,
        IJiraService jira,
        IGitService git,
        IBuildTestService buildTest,
        ICodeAgentService agents,
        FileStateStore store,
        IOptions<PipelineOptions> opts,
        ILoggerFactory lf)
    {
        var orch = new Orchestrator.PipelineOrchestrator(jira, git, buildTest, agents, store, opts, lf.CreateLogger<Orchestrator.PipelineOrchestrator>());
        var ctx = await jira.GetStoryAsync(storyKey);
        ctx.StoryKey = storyKey;
        // Load approved designs from .pipeline/state/<key>/
        ctx.TechnicalDesignContent = await store.LoadArtifactAsync(storyKey, "technical-design.md");
        ctx.TestDesignContent = await store.LoadArtifactAsync(storyKey, "test-design.md");
        if (ctx.TechnicalDesignContent == null || ctx.TestDesignContent == null)
            throw new InvalidOperationException("Approved designs not found — run previous flow first");

        // Assume we are at TestDesignApproval and human approved
        await orch.TransitionStateAsync(PipelineState.ImplementationRequired);

        // 4. Git: orchestrator creates branch deterministically (AI must not)
        var branch = $"feature/{storyKey}-geometry-validation";
        await git.SwitchBranchAsync("main");
        await git.PullAsync();
        if (!await git.IsWorkingTreeCleanAsync()) throw new InvalidOperationException("Working tree dirty");
        await git.CreateBranchAsync(branch);

        // 5. IMPLEMENTING (agent does code changes, does NOT commit/push)
        await orch.TransitionStateAsync(PipelineState.Implementing);
        var implPrompt = Path.Combine(opts.Value.Paths.PromptDir, "implementation.md");
        var implResult = await agents.InvokeAgentAsync(AgentType.Implementer, ctx, implPrompt);
        if (!implResult.IsValid) throw new InvalidOperationException("Implementation result invalid");
        await orch.TransitionStateAsync(PipelineState.Implemented);

        // 6. BUILDING (deterministic, not AI)
        await orch.TransitionStateAsync(PipelineState.Building);
        var build = await buildTest.BuildAsync(opts.Value.Paths.Solution);
        await store.SaveArtifactAsync(storyKey, "build-result.json",
            System.Text.Json.JsonSerializer.Serialize(build, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!build.Success)
        {
            await orch.TransitionStateAsync(PipelineState.BuildFailed);
            // SAFETY: no auto-repair in MVP — go to human
            // If recovery enabled: var rec = await orch.AttemptRecoveryAsync(1); // max 2
            Console.WriteLine($"[FAIL] Build failed for {storyKey}. See {store.BuildResultFile(storyKey)} — awaiting human.");
            return;
        }

        // 7. TESTING
        await orch.TransitionStateAsync(PipelineState.Testing);
        var test = await buildTest.TestAsync(opts.Value.Paths.Solution);
        await store.SaveArtifactAsync(storyKey, "test-result.json",
            System.Text.Json.JsonSerializer.Serialize(test, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!test.Success)
        {
            await orch.TransitionStateAsync(PipelineState.TestFailed);
            // SAFETY: verifier diagnoses, but does NOT auto-fix in MVP
            var verificationPrompt = Path.Combine(opts.Value.Paths.PromptDir, "verification.md");
            var diag = await agents.InvokeAgentAsync(AgentType.Verifier, ctx, verificationPrompt,
                new Dictionary<string, string> { ["build_output"] = build.Output, ["test_output"] = test.Output, ["diff"] = await git.GetDiffAsync() });
            await store.SaveArtifactAsync(storyKey, "verification-result.md", diag.Output);
            Console.WriteLine($"[FAIL] Tests failed for {storyKey}. Diagnosis: {diag.OutputPath} — awaiting human.");
            return;
        }

        // 8. CODE REVIEW (read-only agent)
        await orch.TransitionStateAsync(PipelineState.CodeReview);
        var reviewPrompt = Path.Combine(opts.Value.Paths.PromptDir, "review.md");
        var review = await agents.InvokeAgentAsync(AgentType.Reviewer, ctx, reviewPrompt,
            new Dictionary<string, string> { ["diff"] = await git.GetDiffAsync(), ["build_output"] = build.Output, ["test_output"] = test.Output });
        await store.SaveArtifactAsync(storyKey, "review-result.json", review.Output);
        // review-result.json schema: { status: PASS|FAIL, findings: [], acceptanceCriteria: [], risks: [], recommendations: [] }
        var status = ExtractStatus(review.Output);
        if (status == "FAIL")
        {
            Console.WriteLine($"[REVIEW FAIL] {storyKey} — human must review {review.OutputPath}");
            return;
        }

        // 9. PR_READY (orchestrator will commit/push/PR only after PASS)
        await orch.TransitionStateAsync(PipelineState.PRReady);
        // SAFETY: commit/push/PR are orchestrator-only, after build+test+review PASS, and require human approval for merge
        // await git.AddFilesAsync(...approved files...);
        // await git.CommitAsync($"{storyKey}: <summary>");
        // await git.PushAsync();
        // await git.CreatePrAsync($"{storyKey}: Validate imported geometry", BuildPrBody(ctx, build, test, review));
        Console.WriteLine($"[PR_READY] {storyKey} ready for PR. Diff: {await git.GetDiffAsync()}");
    }

    private static string ExtractStatus(string reviewJson)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(reviewJson);
            return doc.RootElement.GetProperty("status").GetString() ?? "UNKNOWN";
        }
        catch { return "UNKNOWN"; }
    }

    // Stub for examples that don't need real build
    private sealed class StubBuildTestService : IBuildTestService
    {
        public Task<BuildResult> BuildAsync(string p, string c = "Debug") => Task.FromResult(new BuildResult { Success = true });
        public Task<TestResult> TestAsync(string p, string c = "Debug") => Task.FromResult(new TestResult { Success = true });
        public bool IsStoryRelatedFailure(string o) => true;
    }
}
