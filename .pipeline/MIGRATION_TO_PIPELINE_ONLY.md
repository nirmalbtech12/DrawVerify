# Migration: Move ALL pipeline code under `.pipeline/` (Phase 1 — Impact Analysis)

> **Status:** DONE — Phase 2 executed. No code modified in Phase 1.

## 1. Current Architecture (after recent refactor)

```
DrawVerify/
├── src/
│   ├── DrawVerify.App/                 # WPF net9.0-windows (publish via appsettings.json Publish.Path)
│   ├── DrawVerify.Core/ | .Application | .Infrastructure | .Geometry | .OcctService | .OcctWrapper
│   └── LEMUAgenticPipeline/            # NEW classlib net9.0 — shared deterministic core (moved from .pipeline)
│       ├── Models/PipelineModels.cs
│       ├── Services/{IJiraService, IGitService, IBuildTestService, ICodeAgentService, ILEMUAgenticPipeline.Orchestrator}
│       ├── Configuration/PipelineOptions.cs (Paths.AgentDir=.pipeline/agents)
│       ├── State/FileStateStore.cs
│       ├── Orchestrator/{LEMUAgenticPipeline.Orchestrator.cs, StateMachine.cs}
│       ├── Jira/JiraRestService.cs
│       ├── OpenCode/OpenCodeAgentService.cs (+ Claude/OpenAI stubs)
│       └── LEMUAgenticPipeline.csproj
├── .pipeline/
│   ├── agents/                         # 5 md (single source, pipeline-owned)
│   ├── prompts/{system.md, technical-design.md, ...}
│   ├── schemas/{*.schema.json}
│   ├── state/<JIRA-ID>/                # audit trail per §18
│   ├── LEMUAgenticPipeline.Orchestrator/           # thin CLI host (Exe)
│   │   ├── Program.cs (+ CliGitService, DotNetBuildTestService)
│   │   ├── Examples/StoryFlows.cs
│   │   ├── appsettings.json (Pipeline:Paths:AgentDir=.pipeline/agents, Pipeline:Jira, ModelRouting)
│   │   └── LEMUAgenticLEMUAgenticPipeline.Orchestrator.csproj -> ProjectReference src/LEMUAgenticPipeline
│   └── README.md
├── control-plane/LEMUAgenticPipeline.Web/  # Razor Pages, EF Sqlite pipeline.db
│   ├── Models/PipelineModels.cs (DB), AgentDefinition.cs
│   ├── Data/PipelineDbContext.cs, Hubs/PipelineHub.cs
│   ├── Services/{AgentRegistry, PipelineScheduler, PipelineRunner, PipelineWorker}
│   ├── Pages/Pipeline/{Index, Run, Runs/Index, Runs/Detail}
│   └── LEMUAgenticPipeline.Web.csproj -> ProjectReference src/LEMUAgenticPipeline
└── DrawVerify.sln (src/ folder + control-plane/ folder, includes src/LEMUAgenticPipeline)
```

**References to `src/LEMUAgenticPipeline`:**
- `.pipeline/LEMUAgenticPipeline.Orchestrator/LEMUAgenticLEMUAgenticPipeline.Orchestrator.csproj: <ProjectReference Include="..\..\src\LEMUAgenticPipeline\...">`
- `control-plane/LEMUAgenticPipeline.Web/LEMUAgenticPipeline.Web.csproj: <ProjectReference Include="..\..\src\LEMUAgenticPipeline\...">`
- `DrawVerify.sln: Project(src/LEMUAgenticPipeline) + 2 ProjectReferences`
- No tests yet reference it; `src/LEMUAgenticPipeline` namespaces are `LEMUAgenticLEMUAgenticPipeline.Orchestrator.*` (kept for minimal churn).

## 2. Motivation

- **Pipeline as reusable, project-agnostic infrastructure:** Current `src/LEMUAgenticPipeline` ties deterministic pipeline core to this application. Moving to `.pipeline/LEMUAgenticPipeline` makes it a drop-in reusable module for any repo (copy `.pipeline/` + reference `ICodeAgentService`/`IJiraService`) without dragging `DrawVerify.*` domain code.
- **`src/` reserved for domain/application code:** `src/` should contain only `DrawVerify.Core/Application/Geometry/Occt*` etc. Tools like **Graphify** (`graphify extract ./src --code-only` per `AGENTS.md`) and code-graph/analysis scripts should not see pipeline internals — keeps graph focused on business logic, reduces noise.
- **Single deterministic owner:** `.pipeline/` already owns execution artifacts (`agents/`, `prompts/`, `schemas/`, `state/`). Co-locating the classlib there makes `.pipeline/` the one place for both code and execution (agents/prompts/schemas/state).

## 3. Proposed Architecture (all pipeline code under `.pipeline/`)

```
DrawVerify/
├── src/                                # Product code only (App, Core, Application, Infrastructure, Geometry, Occt*) — Graphify target
├── .pipeline/
│   ├── LEMUAgenticPipeline/            # MOVED classlib (was src/LEMUAgenticPipeline) — single deterministic library (project-agnostic)
│   │   └── LEMUAgenticPipeline.csproj (net9.0)
│   ├── agents/ | prompts/ | schemas/ | state/  # unchanged per §3
│   ├── LEMUAgenticPipeline.Orchestrator/           # thin CLI host -> ProjectReference .pipeline/LEMUAgenticPipeline
│   └── README.md
├── control-plane/LEMUAgenticPipeline.Web/ -> ProjectReference .pipeline/LEMUAgenticPipeline
└── DrawVerify.sln (adds .pipeline/LEMUAgenticPipeline, removes src/LEMUAgenticPipeline)
```

`.pipeline/` becomes **both** execution root (agents/prompts/schemas/state) **and** library location (`.pipeline/LEMUAgenticPipeline/`). `.opencode/` remains provider-only (no agents).

## 4. Impact List

**Create:**
- `.pipeline/LEMUAgenticPipeline/LEMUAgenticPipeline.csproj` (copy of `src/LEMUAgenticPipeline.csproj`, same `PackageReference` Microsoft.Extensions.*)
- `.pipeline/LEMUAgenticPipeline/{Models,Services,Configuration,State,Orchestrator,Jira,OpenCode,Claude,OpenAI}/` (move from `src/LEMUAgenticPipeline/`)

**Modify:**
- `.pipeline/LEMUAgenticPipeline.Orchestrator/LEMUAgenticLEMUAgenticPipeline.Orchestrator.csproj`: `ProjectReference` `..\..\src\LEMUAgenticPipeline\...` → `..\LEMUAgenticPipeline\LEMUAgenticPipeline.csproj`
- `control-plane/LEMUAgenticPipeline.Web/LEMUAgenticPipeline.Web.csproj`: `ProjectReference` `..\..\src\LEMUAgenticPipeline\...` → `..\..\.pipeline\LEMUAgenticPipeline\...` (relative from control-plane)
- `DrawVerify.sln`: remove `Project(src/LEMUAgenticPipeline)`, add `Project(.pipeline/LEMUAgenticPipeline)` under `control-plane` or new `.pipeline` solution folder; keep `src/` folder for product code

**Delete:**
- `src/LEMUAgenticPipeline/` (entire directory) after verify

**No change:**
- Namespaces (`LEMUAgenticLEMUAgenticPipeline.Orchestrator.*` kept) — no `using` churn
- `PipelineOptions.Paths.AgentDir=.pipeline/agents` (already correct, relative to repo root)
- `.pipeline/state`, `.pipeline/prompts`, `.pipeline/schemas` contracts

## 5. Risks & Considerations

- **Tooling assumes `src/` layout:** `dotnet sln` and `dotnet build DrawVerify.sln` handle `.pipeline/` projects fine (tested: `dotnet build .pipeline/LEMUAgenticPipeline` works), but CI scripts that `dotnet build src/DrawVerify.sln` or glob `src/**/*.csproj` will miss the new location — update `docs/deployment.md` and any `native/scripts/` if they scan `src/`.
- **Relative paths:** `control-plane` reference becomes `..\..\.pipeline\...` (two levels up). Works, but `dotnet` on Windows vs WSL path separators already handled (`\` vs `/`). No hard-coded `src/LEMUAgenticPipeline` in code beyond csproj — verified via `grep -r src/LEMUAgenticPipeline --include="*.cs" --include="*.json" --include="*.md"` → only csproj/sln.
- **IDE solution folders:** Adding `.pipeline` folder to `DrawVerify.sln` is non-standard but supported. Ensure `HideSolutionNode` not set.
- **Empty `src/` check:** After move, `src/` still has 7 product projects, so not empty — no deletion needed.

## 6. Step-by-Step Migration Plan (Phase 2)

1. `mkdir -p .pipeline/LEMUAgenticPipeline && cp -r src/LEMUAgenticPipeline/* .pipeline/LEMUAgenticPipeline/`
2. `dotnet sln DrawVerify.sln add .pipeline/LEMUAgenticPipeline/LEMUAgenticPipeline.csproj --solution-folder .pipeline`
3. Update `ProjectReference` in `.pipeline/LEMUAgenticPipeline.Orchestrator` and `control-plane/...Web` to new path
4. `dotnet build DrawVerify.sln` + `dotnet build control-plane/...Web` + `dotnet build .pipeline/LEMUAgenticPipeline.Orchestrator` — fix any `MSB3202`
5. `dotnet sln DrawVerify.sln remove src/LEMUAgenticPipeline/LEMUAgenticPipeline.csproj && rm -rf src/LEMUAgenticPipeline`
6. `dotnet build DrawVerify.sln` again, verify `control-plane` still runs (`dotnet run --project control-plane/...`)
7. Update `docs/PIPELINE_ARCHITECTURE.md` (create if missing) with new diagram + note "`.pipeline` is both library and execution root"
8. Commit: single refactor commit, no logic change

> Awaiting human approval to execute Phase 2.
