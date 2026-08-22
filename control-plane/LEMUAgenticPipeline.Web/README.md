# LEMU Agentic Pipeline — Web Control Plane

**Project:** `control-plane/LEMUAgenticPipeline.Web` (ASP.NET Core 9 Razor Pages, EF Core SQLite, SignalR)  
**Purpose:** Pipeline **control plane** — schedule, monitor, and review executions of the deterministic `LEMUAgenticPipeline` (` .pipeline/LEMUAgenticPipeline` + `.pipeline/LEMUAgenticPipeline.Orchestrator`). Does **not** reimplement orchestration.

---

## Architecture

```
Browser
  ↓
Web UI (Razor Pages) + SignalR
  ↓
Pipeline API (AgentRegistry, PipelineScheduler, PipelineRunner, PipelineWorker)
  ↓
Pipeline DB (SQLite) + FileStateStore (.pipeline/state/<JIRA-ID>/)
  ↓
Background Worker (sequential queue)
  ↓
LEMUAgenticPipeline (deterministic core: StateMachine, JiraRestService, ICodeAgentService, FileStateStore)
  ↓
Jira → ICodeAgentService (OpenCode/Claude) → Git → dotnet build/test → PR
```

**Deterministic-first:** All queue ordering, state, dependency validation, build/test results are deterministic C#. AI only in `ICodeAgentService` (technical/test design, implementation, review). Human approval required where `RequiresHumanApproval=true`.

**Project-agnostic:** Core lives in `.pipeline/LEMUAgenticPipeline` (reusable, not in `src/`). `src/` is domain-only for Graphify (`graphify extract ./src --code-only`).

---

## Project Structure

```
control-plane/LEMUAgenticPipeline.Web/
├── Models/
│   ├── PipelineModels.cs (PipelineRun, PipelineRunAgent, PipelineLog, PipelineSchedule/ScheduleItem)
│   └── AgentDefinition.cs (AgentId/Name/Dependencies/RequiresHumanApproval, DefaultAgents.All)
├── Data/PipelineDbContext.cs
├── Services/
│   ├── AgentRegistry.cs (ValidateSelection with auto-select transitive deps, OrderedForExecution)
│   ├── PipelineScheduler.cs (ParseJiraIds, ValidateAsync with Jira existence, OneTime future-today, transaction, CreateSchedule)
│   ├── PipelineRunner.cs (real Jira+ICodeAgent via LEMUAgenticPipeline, FileStateStore, WaitingForApproval poll, Sanitize, branch/commit/PR)
│   └── PipelineWorker.cs (BackgroundService, transactional dequeue, _gitLock sequential)
├── Hubs/PipelineHub.cs (JoinRun/LeaveRun, Log/RunUpdate)
├── Pages/
│   ├── Pipeline/Index.cshtml (Dashboard)
│   ├── Pipeline/Run.cshtml (Run form + preview)
│   ├── Pipeline/Runs/Index.cshtml (Browse + search/filter)
│   └── Pipeline/Runs/Detail.cshtml (Progress + logs + Approve)
├── Program.cs (AddRazorPages, AddSignalR, AddDbContext Sqlite, Configure PipelineOptions, AddSingleton FileStateStore/IJiraService/ICodeAgentService)
├── appsettings.json (ConnectionStrings:Pipeline, Pipeline:Agents)
└── LEMUAgenticPipeline.Web.csproj -> ProjectReference .pipeline/LEMUAgenticPipeline
```

---

## Features (MVP)

- **Single/Multiple Jira Stories:** `KAN-19` or `KAN-19,KAN-20` → preview list 1..N, sequential execution.
- **Scheduling:** `Immediate` (Queued→Running now) + `OneTime` (HH:mm today, future only, `DateTime.SpecifyKind(Local).ToUniversalTime()`).
- **Agent Selection:** Checkboxes from `AgentRegistry` (`Pipeline:Agents`), `Select All/Clear All/Use Default`, auto-select dependencies (e.g., `Implementation` auto-selects `technical-design`+`test-design`), dependency validation blocks invalid starts.
- **Sequential:** One story at a time (`PipelineWorker.anyRunning` + `_gitLock` + transaction).
- **Persistence:** `PipelineRuns` (RunId, JiraId, ScheduleType, Status, Branch/Commit/PR, SelectedAgentsJson), `PipelineRunAgents` (per-agent status/duration), `PipelineLogs` (timestamp/level/agent/message, SignalR), `PipelineSchedule/Items`.
- **Dashboard:** `/pipeline` (Total/Running/Queued/Completed/Failed/Waiting, recent 10).
- **Run:** `/pipeline/run` (JiraIds, Immediate/OneTime, agents, StopOnFailure, preview → Confirm & Start).
- **Browse:** `/pipeline/runs` (`q` JiraId, `from`/`to` date, `status`, pagination).
- **Detail:** `/pipeline/runs/{runId}` (basic info, progress timeline ✓▶⏸○, skipped agents, detailed logs with All/Error/Warn filters, Approve/Reject buttons when `WaitingForApproval`, SignalR live + 5s polling fallback, never exposes `JIRA_TOKEN`).

---

## Routes

| Route | Page |
|-------|------|
| `/pipeline` | Dashboard |
| `/pipeline/run` | Run form |
| `/pipeline/runs` | Browse (search/filter) |
| `/pipeline/runs/{runId}` | Job detail + logs + approval |
| `/hubs/pipeline` | SignalR |
| `/health` | Health check |

---

## Configuration

`appsettings.json` (or env `Pipeline__*`):
```json
{
  "ConnectionStrings": { "Pipeline": "Data Source=pipeline.db" },
  "Pipeline": {
    "Agents": {
      "Agents": [
        { "AgentId": "technical-design", "Name": "Technical Design", "Dependencies": [], "RequiresHumanApproval": true, "ExecutionOrder": 1, "Enabled": true },
        { "AgentId": "test-design", "Name": "Test Design", "Dependencies": ["technical-design"], "RequiresHumanApproval": true, "ExecutionOrder": 2 },
        { "AgentId": "implementation", "Name": "Implementation", "Dependencies": ["technical-design","test-design"], "ExecutionOrder": 3 },
        { "AgentId": "test", "Name": "Test", "Dependencies": ["implementation"], "ExecutionOrder": 4 },
        { "AgentId": "code-review", "Name": "Code Review", "Dependencies": ["test"], "ExecutionOrder": 5 },
        { "AgentId": "jira-update", "Name": "Jira Update", "Dependencies": ["code-review"], "Enabled": false, "ExecutionOrder": 6 }
      ],
      "DefaultSelected": ["technical-design"]
    }
  }
}
```
Core pipeline config (Jira, ModelRouting) from `.pipeline/LEMUAgenticPipeline.Orchestrator/appsettings.json` + env `Pipeline__Jira__BaseUrl`, `JIRA_TOKEN`, `Pipeline__ModelRouting__TechnicalDesigner`.

---

## Running

```bash
# From repo root
dotnet build DrawVerify.sln
dotnet run --project control-plane/LEMUAgenticPipeline.Web
# → http://localhost:5000/pipeline

# Custom port
dotnet run --project control-plane/LEMUAgenticPipeline.Web --urls http://localhost:5000

# Watch
dotnet watch --project control-plane/LEMUAgenticPipeline.Web run
```

**CLI orchestrator (separate, for direct runs):**
```bash
dotnet run --project .pipeline/LEMUAgenticPipeline.Orchestrator -- run KAN-19
dotnet run --project .pipeline/LEMUAgenticPipeline.Orchestrator -- approve KAN-19 --state TechDesignApproval --decision APPROVED
```

---

## Database

- **SQLite** `control-plane/LEMUAgenticPipeline.Web/pipeline.db` (`EnsureCreated` MVP, next: `dotnet ef migrations add`).
- **Concurrency:** `BeginTransactionAsync` in `CreateScheduleAsync` + `TickAsync` dequeue (mark `Running` atomically), `SemaphoreSlim _gitLock` for workspace.

---

## Human Approval

Agents with `RequiresHumanApproval=true` (Technical Design, Test Design) transition to `WaitingForApproval`. Web shows **Approve/Reject** buttons → `POST .../Detail?handler=Approve` → writes `.pipeline/state/<Jira>/approval.json` (`{decision, runId}`) + updates `PipelineRun/Agent` + `PipelineLogs`. Worker polls `approval.json` + DB (24h timeout).

---

## Security

- Logs sanitized (`Sanitize()` redacts `JIRA_TOKEN`, `Bearer`, `ATATT*`).
- No `JIRA_TOKEN` in `appsettings.json` (env `JIRA_TOKEN` only).
- `.gitignore` excludes `pipeline.db*`.

---

## Definition of Done (MVP)

- [x] Single/multiple Jira IDs, Immediate/OneTime (future today), sequential, StopOnFailure
- [x] Agent selection (config-driven, dependency auto-select, persisted `SelectedAgentsJson`)
- [x] Dashboard, Browse (Jira/date/status), Detail (progress, logs, SignalR)
- [x] Real `LEMUAgenticPipeline` via `IJiraService`/`ICodeAgentService`/`FileStateStore` (fallback simulation if model not configured)
- [x] Separate project (`control-plane/`), not in `DrawVerify.App` publish

**Future (not MVP):** Recurring schedules (Daily/Weekly/Cron), parallel stories, RBAC, analytics, Kubernetes workers.

---

## Extensibility

Add agent: `.pipeline/agents/<new>.md` + `.pipeline/prompts/<new>.md` → extend `AgentDefinition` list and `PipelineRunner.MapAgent`. Model provider: change `Pipeline:ModelRouting` (`anthropic/claude-3-5-sonnet`, `openai/gpt-4o`) or implement `LEMUAgenticPipeline.Claude/OpenAI` `: ICodeAgentService`.
