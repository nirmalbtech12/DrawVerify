# Pipeline Orchestrator

Deterministic orchestrator for DrawVerify agentic pipeline (see `docs/agentic-pipeline.md`).

## Quick start

```bash
dotnet build .pipeline/LEMUAgenticPipeline.Orchestrator/LEMUAgenticPipeline.Orchestrator.csproj
dotnet run --project .pipeline/LEMUAgenticPipeline.Orchestrator -- run DV-TEST-001
# ... review .pipeline/state/DV-TEST-001/technical-design.md ...
dotnet run --project .pipeline/LEMUAgenticPipeline.Orchestrator -- approve DV-TEST-001 --state TechDesignApproval --decision APPROVED
```

## Where safety limits are enforced

| Limit | Enforcement |
|---|---|
| No auto-merge | `PRReady -> PRCreated -> HumanReview` requires human APPROVED; orchestrator never calls merge API |
| No auto-repair (MVP) | `BuildFailed/TestFailed -> Verification` only diagnoses; `AttemptRecoveryAsync` returns `ShouldContinue=false` when `MaxRecoveryAttempts==0` |
| Max 2 recoveries | `Safety.MaxRecoveryAttempts` (appsettings.json) checked in `AttemptRecoveryAsync`; 3rd attempt rejected |
| No Jira mutation by AI | `IJiraService` only called by orchestrator; agents receive read-only `StoryContext` |
| No Git by AI | `IGitService` only called by orchestrator; agents return diff/results, never `git commit` |
| Branch deletion disabled | `Safety.BranchDeletionDisabled` — orchestrator never deletes branches |
| Unlimited loops disabled | `Safety.UnlimitedAgentLoopsDisabled` — `SkipStageAsync` throws |

## Model routing (no hard-coding)

`appsettings.json` section `Pipeline:ModelRouting` maps `AgentType -> model string`. Override via env:
`Pipeline__ModelRouting__TechnicalDesigner="my-model"`. Orchestrator reads `IOptions<PipelineOptions>` on every `InvokeAgentAsync`.

## Adding a new agent

1. Add file `.opencode/agents/<new>.md`
2. Add prompt `.pipeline/prompts/<new>.md`
3. Extend `AgentType` enum, `ModelRoutingConfiguration`, `StateMachine` transitions, and `OpenCodeAgentService` validation mapping.

## State & artifacts

Per story: `.pipeline/state/<JIRA-ID>/` — approval decision is agent-suffixed and generated FROM the JIRA-specific source artifact (no separate request file; the `<agent>.md` itself is validated):

```
jira.json
technical-design.md                  # JIRA-specific source for technical-design (content to validate)
test-design.md                       # JIRA-specific source for test-design (content to validate)
implementation-result.json
build-result.json
test-result.json
review-result.json
pipeline-state.json
execution-log.json
technical-design-approval.json       # human writes, generated FROM technical-design.md (includes sourceArtifact/sourceHash)
test-design-approval.json            # human writes, generated FROM test-design.md (includes sourceArtifact/sourceHash)
approval.json                        # legacy compat, also written
```
