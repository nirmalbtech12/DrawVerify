# Agentic Pipeline: Current Architecture and Runtime Behavior

## Scope

The agentic pipeline is separate from the DrawVerify product code. Product
projects remain under `src/`; pipeline code is under `.pipeline/` including the
web control plane at `.pipeline/LEMUAgenticPipeline.Web` (moved from
`control-plane/LEMUAgenticPipeline.Web` to avoid duplicate
`config/prompts/agents` copies; `control-plane/` is removed, use
`.pipeline/run.sh`).
Both are included in `DrawVerify.sln`, but Graphify is intentionally scoped to
`./src`.

This document describes the current code, including unfinished and simulated
paths. It is not a claim that the repository provides end-to-end automated Git,
pull-request, merge, or Jira delivery.

## Repository structure

```text
DrawVerify/
├── .pipeline/
│   ├── LEMUAgenticPipeline/                  # net9.0 shared library
│   │   ├── Configuration/                    # PipelineOptions
│   │   ├── Jira/                             # JiraRestService
│   │   ├── OpenCode/                         # ICodeAgentService implementation
│   │   ├── Orchestrator/                     # state machine and orchestrator API
│   │   ├── Services/                         # integration contracts
│   │   └── State/                            # FileStateStore
│   ├── LEMUAgenticPipeline.Orchestrator/     # net9.0 CLI host
│   ├── LEMUAgenticPipeline.Web/              # ASP.NET Core 9 Razor Pages app (moved from control-plane)
│   ├── agents/                               # checked-in agent definitions (single source; .opencode/agents is symlink)
│   ├── prompts/                              # task and system prompts (single source)
│   ├── schemas/                              # output-schema files (single source)
│   ├── state/<JIRA-ID>/                      # per-story artifacts at runtime
│   └── run.sh                                # run web: dotnet run --project LEMUAgenticPipeline.Web
├── src/                                      # DrawVerify product code
└── docs/
```

The checked-in agent definitions are in `.pipeline/agents` — the single source
of truth. `Pipeline:Paths:AgentDir` is `.pipeline/agents` in
`.pipeline/LEMUAgenticPipeline.Orchestrator/appsettings.json` and
`.pipeline/LEMUAgenticPipeline.Web/appsettings.json` (code default is also
`.pipeline/agents`). `.opencode/agents` is a symlink
(`.opencode/agents -> ../.pipeline/agents`) retained only so
the `opencode --agent <name> --dir <repoRoot>` CLI can still resolve agents via
its default `.opencode/agents` location. `OpenCodeAgentService` does not read
`AgentDir` for the prompt (it passes `--agent`/`--file`), it only uses the alias.
There is no longer a `control-plane/.../.pipeline` duplicate — web now lives at
`.pipeline/LEMUAgenticPipeline.Web` and resolves all pipeline paths via repo root
(`Program.cs: ResolveRepoRoot()` + `PostConfigure` absolute `Paths`). When the
agent file is absent the service substitutes `# Agent: <AgentType>`.

## Components

```text
Browser
  ↓
LEMUAgenticPipeline.Web (.pipeline/LEMUAgenticPipeline.Web)
  ├── Razor Pages and SignalR
  ├── SQLite run/schedule/log records
  ├── PipelineWorker (background queue consumer)
  └── PipelineRunner (web-stage execution)
  ↓ ProjectReference
LEMUAgenticPipeline (.pipeline/LEMUAgenticPipeline)
  ├── JiraRestService and FileStateStore
  ├── StateMachine and PipelineOrchestrator API
  └── ICodeAgentService / OpenCodeAgentService
  ↓
Jira REST API, OpenCode process, and .pipeline/state/<JIRA-ID>/ artifacts
```

The CLI host is a separate entry point. It uses the same shared library and
offers `run` and `approve` commands.

## Shared state model

`StateMachine` defines and validates the following transitions for one Jira
story:

```text
Discovered → Validated → TechDesignRequired → TechDesignReady
  → TechDesignApproval → TestDesignRequired → TestDesignReady
  → TestDesignApproval → ImplementationRequired → Implementing
  → Implemented → Building → Testing → CodeReview → PRReady
  → PRCreated → HumanReview → Merged → Done
```

It also permits these failure paths:

```text
Building → BuildFailed → Verification → Blocked or Building
Testing  → TestFailed  → Verification → Blocked or Building
```

`PipelineOrchestrator` provides operations to start a story, perform a
validated transition, save state, request approval by polling the
agent-suffixed `<agent>-approval.json` (e.g., `technical-design-approval.json`)
— each generated FROM its JIRA-specific source artifact (e.g.,
`technical-design.md` → `technical-design-approval.json`, with `sourceHash`
traceability). No separate `approval-request.md` is generated; the agent's
own `<agent>.md` (e.g., `technical-design.md`) is the full content to be
validated. It also supports abort and verifier diagnosis. It writes `jira.json`,
`pipeline-state.json`, and `execution-log.json` through `FileStateStore` when
those operations are called.

The state machine is not currently driven end-to-end by either host:

- The CLI `run` command calls the example `RunUntilTechDesignApprovalAsync`
  flow, which stops after producing technical design and entering its approval
  state.
- The web runner does not use `PipelineOrchestrator`; it tracks selected web
  agents in its own database status model.

## Agents, prompts, and artifacts

The shared `AgentType` values are TechnicalDesigner, TestDesigner,
Implementer, Verifier, and Reviewer.

**Prompt.md vs Agent.md separation (no duplication):**
- `Prompt.md` (`/.pipeline/prompts/*.md`) — task-specific prompt/context only (Jira key/summary/requirement/ACs with `{{placeholders}}`, approved designs). No coding commands, workflow, or tool instructions.
- `Agent.md` (`/.pipeline/agents/*.md`) — agent behavior, execution rules, tools, and **standard JSON Project Response schema** (no task inputs). No duplicated task text.
- `system.md` — deterministic orchestration, human-controlled approval, never bypass gates; does not duplicate Agent.md schema.

**Standard JSON Project Response (mandatory, no alternative):**
Every `Agent.md` defines the complete schema and requires the final Project
Response in exactly this format (valid JSON inside `<response>`):

```xml
<response>
{
  "status": "SUCCESS | FAILURE",
  "response": "...",
  "reason": "..."
}
</response>
```
- `status` is mandatory, must be `SUCCESS` or `FAILURE`.
- `SUCCESS` requires `response` (non-empty string) — must be the complete artifact content per existing data contract (e.g., technical-design markdown, implementation JSON string). Keep existing contracts same, only wrapped as JSON string.
- `FAILURE` requires `reason` (non-empty string).
- Content inside `<response>` must be valid JSON. No alternative or free-form Project Response is allowed.

**Deterministic response processing (no LLM for status):**
`OpenCodeAgentService` / `OpenAIAgentService` **do not** use free-form text or LLM to determine status:
1. Read `<response>` block from agent output (`stdout`, fallback to file artifact).
2. Parse and validate outer JSON (status/required fields) and inner data contract (existing headings/JSON).
3. Write parsed result via existing response-file mechanism (`FileStateStore.SaveArtifactAsync` / `StoreOutputAsync` → `technical-design.md`, `test-design.md`, `implementation-result.json`, etc.).
4. Update run status deterministically: `SUCCESS` → write `response` → `AgentResult.IsValid=true, Status=SUCCESS` → `Run` continues; `FAILURE` → write `reason` → `Status=FAILURE, IsValid=false` → `Run=FAILURE`; missing/invalid → write error → `Status=INVALID_RESPONSE, IsValid=false` → `Run=FAILURE`.

**OpenCode execution (minimal, no hardcoded prompt duplication):**
`OpenCodeAgentService.InvokeAgentAsync` reads `Prompt.md` via `promptFilePath` (`Pipeline:Paths:PromptDir` + `MapAgent`), renders `{{placeholders}}`, builds `fullPrompt = system + task prompt + metadata`, saves as `<agent>-prompt.md` for audit, and passes it as the OpenCode prompt via `--file <promptArtifactPath>` (`opencode run "..." --file <prompt> --model <model> --agent <kebab> --dir <repoRoot> --auto`). No prompt content is hardcoded in C#; Prompt.md is the single source.

The shared `AgentType` values are TechnicalDesigner, TestDesigner,
Implementer, Verifier, and Reviewer. For file-producing agents
(TechnicalDesigner → `technical-design.md`, TestDesigner → `test-design.md`),
the service still removes any stale artifact before invocation, but the authoritative
output is the parsed `<response>` `response` string, written deterministically via `FileStateStore`.

| Agent type | Task prompt (Prompt.md) | Stored output (via <response> `response`) | Output source |
|---|---|---|---|
| TechnicalDesigner | `technical-design.md` | `technical-design.md` | `<response>` `response` markdown, validated for required headings |
| TestDesigner | `test-design.md` | `test-design.md` | `<response>` `response` markdown, validated for required headings |
| Implementer | `implementation.md` | `implementation-result.json` | `<response>` `response` JSON string, validated as JSON |
| Verifier | `verification.md` | `verification-result.md` | `<response>` `response` markdown, non-empty |
| Reviewer | `review.md` | `review-result.json` | `<response>` `response` JSON string, validated as JSON |

Prompts are stored before invocation as `<agent>-prompt.md`. The verifier
recovery path additionally writes `verification-attempt-<n>.md`.

Output validation is deterministic and minimal (inner data contract):

- Technical and test designs are checked for selected Markdown headings
  (`## Jira`, `## Requirement`, `## Proposed Changes`, `## Risks`,
  `## Acceptance Criteria Mapping` for technical-design; `## Jira`,
  `## Test Strategy`, `## Unit Tests`, `## Acceptance Criteria` for test-design) — now on the inner `response` string, not outer JSON. On failure, `AgentResult.Status` is `INVALID_RESPONSE` and `PipelineRunner` throws `Agent {id} output failed schema validation (...)`, marking `Run=FAILURE`.
- Implementer and reviewer inner `response` is checked as JSON-parseable
  (`INVALID_RESPONSE: output is not valid JSON` on failure).
- Verifier inner `response` only needs to be non-empty.
- If a configured schema file is missing, inner validation succeeds without a schema-based check (outer `<response>` JSON validation still applies).
- Empty output, missing `<response>` block, or invalid outer JSON throws `Missing or invalid <response> block` / `Invalid <response> JSON` and writes error via `StoreOutputAsync`, marking `Run=FAILURE`.

`OpenCodeAgentService` and `OpenAIAgentService` are implemented
model-provider adapters. Both hosts select their `ICodeAgentService` through
`Pipeline:Provider`, which accepts `OpenCode` or `OpenAI` and defaults to
`OpenCode`. The OpenAI provider registers a singleton `HttpClient` and calls
the Responses API; it reads its key from `Pipeline:OpenAI:ApiKey` or the
configured `Pipeline:OpenAI:ApiKeyEnv` environment variable, and sets the
Responses API `store` field from `Pipeline:OpenAI:StoreResponses` (default
`false`). The Claude adapter throws `NotImplementedException`.
Model names are read from `Pipeline:ModelRouting`; `OpenCodeAgentService`
additionally maps two named Nemotron aliases in code and otherwise passes the
configured value through as the model argument.

## Web control plane

`LEMUAgenticPipeline.Web` is an ASP.NET Core 9 Razor Pages application using
EF Core SQLite and SignalR. It persists `PipelineRun`, `PipelineRunAgent`,
`PipelineLog`, `PipelineSchedule`, and `PipelineScheduleItem` entities.
Startup calls `Database.EnsureCreated()`; migrations are present in the project
but are not applied by startup.

The web app provides these routes:

| Route | Purpose |
|---|---|
| `/pipeline` | Dashboard |
| `/pipeline/run` | Schedule submission |
| `/pipeline/runs` | Run history |
| `/pipeline/runs/{runId}` | Run details, logs, and approval action |
| `/hubs/pipeline` | SignalR hub |
| `/health` | JSON health response |

`PipelineScheduler` accepts comma-separated Jira IDs, supports `Immediate` or
same-day future `OneTime` scheduling, validates the configured agent list, and
creates runs and schedule items in a transaction. Jira validation treats a 404
as an error but logs and permits scheduling when Jira is unavailable for other
reasons.

`PipelineWorker` polls every two seconds. It claims one due schedule item in a
database transaction, then serializes execution with a static `SemaphoreSlim`
Git lock. It invokes `PipelineRunner` for the selected stages in configured
execution order.

The current configured stages are technical design, test design,
implementation, test, code review, and a disabled Jira-update stage. The
default selection ends at `test`; code review and Jira update are not selected
by default.

For technical-design, test-design, implementation, and code-review stages,
the web runner retrieves the Jira story and calls `ICodeAgentService` when the
configured prompt file exists; prompt/schema/agent paths are resolved
relative to the web app's current directory while `FileStateStore`
resolves `StateRoot` to the repository root via `DrawVerify.sln`/`.git` walk.
The `test` stage is simulated: it waits briefly and randomly reports a
simulated failure. Other unmapped stages are also simulated by delay.

Some non-`InvalidOperationException` agent failures enter a fallback path. For
technical design, test design, implementation, or code review, that path lets
the run continue only if the corresponding artifact already exists. Jira
errors, invalid-schema results (now reported as
`Agent {id} output failed schema validation (INVALID_SCHEMA: missing ...)`)
, OpenCode non-zero exits, empty output, and timeouts do not use that fallback
and fail the current stage.

After a completed implementation stage, the web runner generates a synthetic
branch name and random commit hash. After a Jira-update stage, it generates a
synthetic GitHub pull-request URL and number. It performs no Git branch,
commit, push, PR, merge, or Jira-update operation.

Technical-design and test-design stages require web approval when their agent
definition has `RequiresHumanApproval=true`. No separate
`<agent>-approval-request.md` is created; the agent's own JIRA-specific output
(e.g., `technical-design` → `technical-design.md`, `test-design` →
`test-design.md`) is the full content to be validated. For each gated agent the
page writes the corresponding `.pipeline/state/<JIRA-ID>/<agent>-approval.json`
(e.g., `technical-design-approval.json`, `test-design-approval.json`, with
`sourceArtifact`/`sourceHash` traceability) and updates the waiting database record; the
running worker polls the agent-suffixed file (only that file, no generic reuse) + DB
for up to 24 hours. The worker also cleans per-agent `*-approval.json` at run
start so a prior run's rejected decision cannot bleed into the next run. The
**Pipeline Progress** section in `/Pipeline/Runs/{runId}` also shows a per-agent
`View <artifact>` link (e.g., `View technical-design.md` /
`View test-design.md`) when the file exists under
`.pipeline/state/<JIRA>/`; it is served via
`GET /Pipeline/Runs/{runId}?handler=Artifact&agentId=<agent>`
(`FileStateStore.ArtifactFileForAgent()` + `LoadArtifactAsync()` with
`SanitizeAgentId()`/path-traversal guard).

**On human approval (`APPROVED`)**, the approved markdown is synced to Jira via
`IJiraService.UpdateIssueFieldAsync` (per `JiraOptions.GetFieldForAgent` — configurable via `Pipeline:Jira:ImplementationField` / `TestDesignField`, similar to `AcceptanceCriteriaFields`; fallback to `FileStateStore.JiraFieldForAgent`):
- `technical-design` → `Pipeline:Jira:ImplementationField` (default `customfield_10168`, label **Implementation**)
- `test-design` → `Pipeline:Jira:TestDesignField` (default `customfield_10201`, label **Test Design**)
Content is converted to ADF (`MarkdownToAdf`) for `rest/api/3/issue/{key}` `PUT {"fields":{field: adf}}` with fallback to raw string; failures are logged (`Warn`) but do not fail the approval (approval file + DB already recorded). This occurs in `Detail.OnPostApproveAsync` (web UI, via `IOptions<PipelineOptions>`), `PipelineRunner` after `WaitForApprovalAsync` (`_opts.Jira.GetFieldForAgent`), `PipelineOrchestrator.RequestHumanApprovalAsync` after `PollApprovalAsync` (`_opts.Jira`), and `Program.cs` `approve` CLI (`IOptions<PipelineOptions>`). The web runner does not expose a human-review/merge stage.

Run-log messages are sanitized before they are stored and broadcast: the code
redacts Basic/Bearer header values, `ATATT` token patterns, and values written
as `JIRA_TOKEN=<value>`.

## Safety configuration and enforcement

`Pipeline:Safety` contains the following settings:

- `MaxRecoveryAttempts`
- `AutoMergeDisabled`
- `ProductionDeploymentDisabled`
- `JiraModificationDisabled`
- `BranchDeletionDisabled`
- `UnlimitedAgentLoopsDisabled`

Only `MaxRecoveryAttempts` and `UnlimitedAgentLoopsDisabled` are currently
checked by `PipelineOrchestrator`. Recovery invokes the verifier for diagnosis
and always returns `ShouldContinue=false`; it does not make automated repairs.
The other settings are configuration values with no enforcing call sites in the
current code. Similarly, the rule that AI must not make Git or Jira changes is
expressed by prompts and architecture, not by a process-level permission
boundary around the spawned OpenCode command.

## Configuration and operation

Both hosts load
`.pipeline/LEMUAgenticPipeline.Orchestrator/appsettings.json` (single source; web
resolves via repo root `Path.Combine(repoRoot, ".pipeline/...")`); the web host
also loads its local `.pipeline/LEMUAgenticPipeline.Web/appsettings.json`. Each
registers an environment-variable provider with the literal prefix `Pipeline_`.
Because that provider strips its prefix before configuration binding, ensure an
override still includes the `Pipeline` section after the prefix (for example,
`Pipeline_Pipeline__Jira__BaseUrl`) or confirm the resulting configuration at
startup. Jira uses the configured `TokenEnv`, which defaults to `JIRA_TOKEN`.
Do not store credential values in checked-in settings.

`Pipeline:Provider` controls agent-service registration. The checked-in value
is `OpenCode`; set it to `OpenAI` only after providing an OpenAI API key through
`Pipeline:OpenAI:ApiKey` in a secure configuration provider or the configured
`OPENAI_API_KEY` environment variable.

Run from the repository root:

```bash
dotnet build DrawVerify.sln
dotnet run --project .pipeline/LEMUAgenticPipeline.Web  # or .pipeline/run.sh
```

The CLI commands are:

```bash
dotnet run --project .pipeline/LEMUAgenticPipeline.Orchestrator -- run KAN-19
dotnet run --project .pipeline/LEMUAgenticPipeline.Orchestrator -- approve KAN-19 --state TechDesignApproval --decision APPROVED
```

## Current operational boundary

The implemented system is a planning, artifact-generation, scheduling,
approval, and observability MVP. It is not a delivery pipeline: web testing is
simulated, the web runner bypasses the shared state-machine orchestration,
Git/PR/Jira actions are synthetic or no-op, and the CLI flow stops at the first
approval gate. Do not use its run records as evidence that a branch, commit,
pull request, merge, or Jira update occurred.
