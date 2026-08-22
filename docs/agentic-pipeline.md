# Agentic Pipeline: Current Architecture and Runtime Behavior

## Scope

The agentic pipeline is separate from the DrawVerify product code. Product
projects remain under `src/`; pipeline code is under `.pipeline/` and the web
control plane is under `control-plane/`. Both are included in
`DrawVerify.sln`, but Graphify is intentionally scoped to `./src`.

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
│   ├── agents/                               # checked-in agent definitions
│   ├── prompts/                              # task and system prompts
│   ├── schemas/                              # output-schema files
│   └── state/<JIRA-ID>/                      # per-story artifacts at runtime
├── control-plane/LEMUAgenticPipeline.Web/    # ASP.NET Core 9 Razor Pages app
├── src/                                      # DrawVerify product code
└── docs/
```

The checked-in agent definitions are in `.pipeline/agents`. However, the
currently loaded orchestrator configuration sets `Pipeline:Paths:AgentDir` to
`.opencode/agents`. `OpenCodeAgentService` therefore looks there at runtime;
when the requested file is absent it substitutes `# Agent: <AgentType>` as the
agent definition. The `PathOptions` code default is `.pipeline/agents`, so the
effective path depends on configuration.

## Components

```text
Browser
  ↓
LEMUAgenticPipeline.Web
  ├── Razor Pages and SignalR
  ├── SQLite run/schedule/log records
  ├── PipelineWorker (background queue consumer)
  └── PipelineRunner (web-stage execution)
  ↓ ProjectReference
LEMUAgenticPipeline
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
validated transition, save state, request approval by polling `approval.json`,
abort, and obtain diagnosis from the verifier agent. It writes `jira.json`,
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
Implementer, Verifier, and Reviewer. `OpenCodeAgentService` creates a prompt
from the system prompt, the configured agent-definition path, the selected
task prompt, Jira context, configured model name, and optional custom input.
It invokes `opencode run` as a child process. For file-producing agents
(TechnicalDesigner → `technical-design.md`, TestDesigner → `test-design.md`),
it removes any stale artifact before invocation, then prefers the on-disk
artifact written by the agent over stdout chatter (stdout is kept for diagnostics).
The resolved output is then validated and stored via `FileStateStore`.

| Agent type | Task prompt | Stored output | Output source |
|---|---|---|---|
| TechnicalDesigner | `technical-design.md` | `technical-design.md` | file artifact preferred, stdout fallback |
| TestDesigner | `test-design.md` | `test-design.md` | file artifact preferred, stdout fallback |
| Implementer | `implementation.md` | `implementation-result.json` | stdout (JSON) |
| Verifier | `verification.md` | `verification-result.md` | stdout |
| Reviewer | `review.md` | `review-result.json` | stdout (JSON) |

Prompts are stored before invocation as `<agent>-prompt.md`. The verifier
recovery path additionally writes `verification-attempt-<n>.md`.

Output validation is deliberately minimal:

- Technical and test designs are checked for selected Markdown headings
  (`## Jira`, `## Requirement`, `## Proposed Changes`, `## Risks`,
  `## Acceptance Criteria Mapping` for technical-design; `## Jira`,
  `## Test Strategy`, `## Unit Tests`, `## Acceptance Criteria` for test-design).
  On failure, `OpenCodeAgentService` sets `AgentResult.Status` to
  `INVALID_SCHEMA: missing required sections: ...` and logs the missing
  headings; `PipelineRunner` surfaces that status in its
  `Agent {id} output failed schema validation (...)` exception.
- Implementer and reviewer output is only checked as JSON-parseable
  (`INVALID_SCHEMA: output is not valid JSON` on failure).
- Verifier output only needs to be non-empty.
- If a configured schema file is missing, validation succeeds without a
  schema-based check.
- Empty output (and, for file agents, missing/empty on-disk artifact)
  throws `opencode produced empty output and no artifact at ...`.

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
definition has `RequiresHumanApproval=true`. The page writes
`.pipeline/state/<JIRA-ID>/approval.json` and updates the waiting database
record; the running worker polls both locations for up to 24 hours. The web
runner does not expose a human-review/merge stage.

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
`.pipeline/LEMUAgenticPipeline.Orchestrator/appsettings.json`; the web host
also loads its local `appsettings.json`. Each registers an environment-variable
provider with the literal prefix `Pipeline_`. Because that provider strips its
prefix before configuration binding, ensure an override still includes the
`Pipeline` section after the prefix (for example,
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
dotnet run --project control-plane/LEMUAgenticPipeline.Web
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
