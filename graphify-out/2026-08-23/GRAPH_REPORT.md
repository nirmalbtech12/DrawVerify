# Graph Report - DrawVerify  (2026-08-23)

## Corpus Check
- 245 files · ~61,152 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1145 nodes · 1651 edges · 124 communities (100 shown, 24 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 70 edges (avg confidence: 0.77)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `842224bc`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- DrawVerify.Core.Models
- DrawVerify.Core.Interfaces
- DrawVerify.Application.Services
- DrawVerify.Application.Verification
- DrawVerify.App
- DrawVerify.Infrastructure.Persistence
- DrawVerify.Geometry.Models
- DrawVerify.Application.Verification.GdtRules
- DrawVerify.Core.Enums
- DrawVerify.Core.csproj
- .RunFromTestDesignApprovalToPrReadyAsync
- OcctGeometryService.cs
- FileStateStore
- OcctShapeSafeHandle
- DrawVerify.Application.Workflow
- PipelineRun
- CliGitService
- DrawVerify.Infrastructure.Signatures
- DrawVerify.Infrastructure.Logging
- UserControl
- DrawVerify.Core.Configuration
- DrawVerify.Infrastructure.FileStorage
- DrawVerify.Application.Fingerprinting
- DrawVerify.Infrastructure.DataProtection
- DrawVerify.Infrastructure.Pdf
- UserControl
- DrawVerify.App.Views
- StoryContext
- LEMUAgenticPipeline.Services
- .ValidateAsync
- InAppNotificationService.cs
- OcctToGeometryMapper.cs
- CompositionRoot.cs
- GeometryType.cs
- JiraRestService
- Exception
- AGENTS.md
- DrawVerify.Application.Tests
- DrawVerify.sln
- Integration Tests
- SKILL.md
- Technical Design Detail — KAN-20 US-1.1.2: Extract STEP Measurements
- LEMUAgenticPipeline.Orchestrator
- PipelineWorker
- Migration: Move ALL pipeline code under `.pipeline/` (Phase 1 — Impact Analysis)
- LEMUAgenticPipeline.Web
- DrawVerify.Infrastructure.Tests
- Architecture
- LEMUAgenticPipeline.csproj
- Pipeline Orchestrator
- DrawVerify.Geometry.Tests.csproj
- IBuildTestService
- Deployment — OCCT native DLL distribution + AOT validation
- Fingerprint Policy
- InitialCreate
- UserControl
- UserControl
- UserControl
- UserControl
- Engineering Rules & Known Gaps
- MVP GD&T Subset
- LGPL Compliance — OCCT
- graphify.js
- DrawVerify
- UserControl
- DrawVerify.Core.Tests.csproj
- DrawVerify.Infrastructure.Tests.csproj
- implementation-result.schema.json
- review-result.schema.json
- technical-design.schema.json
- test-design.schema.json
- Detail.cshtml
- OcctGeometryServiceTests.cs
- MockGeometryServiceTests.cs
- DrawVerify.OcctWrapper
- ErrorModel
- IndexModel
- Pipeline/Index.cshtml
- Run.cshtml
- Runs/Index.cshtml
- Privacy.cshtml
- Agentic Pipeline: Current Architecture and Runtime Behavior
- .opencode/README.md
- Pipeline (deterministic owner)
- Technical Design Detail — KAN-19 US-1.1.1: Upload STEP File
- LEMU Agentic Pipeline — Web Control Plane
- http
- DrawVerify.OcctService.Tests.csproj
- PipelineOrchestrator
- LEMUAgenticPipeline.Web/run.sh
- Test Design — KAN-19 US-1.1.1: Upload STEP File
- CliGitService
- .pipeline/run.sh
- ICodeAgentService
- DrawVerify.App.ViewModels
- clean-pipeline.sh
- Technical Design
- .opencode/agents/test-designer.md

## God Nodes (most connected - your core abstractions)
1. `FileStateStore` - 38 edges
2. `StoryContext` - 28 edges
3. `JiraRestService` - 27 edges
4. `AgentType` - 27 edges
5. `PipelineOrchestrator` - 26 edges
6. `Technical Design Detail — KAN-19 US-1.1.1: Upload STEP File` - 22 edges
7. `OpenCodeAgentService` - 21 edges
8. `OpenAIAgentService` - 19 edges
9. `Technical Design Detail — KAN-20 US-1.1.2: Extract STEP Measurements` - 19 edges
10. `LEMUAgenticPipeline.Services` - 18 edges

## Surprising Connections (you probably didn't know these)
- `UserControl` --references--> `DocumentListViewModel`  [INFERRED]
  src/DrawVerify.App/Views/DocumentListView.xaml → src/DrawVerify.App/ViewModels/DocumentListViewModel.cs
- `CliGitService` --implements--> `IGitService`  [EXTRACTED]
  .pipeline/LEMUAgenticPipeline.Orchestrator/Program.cs → .pipeline/LEMUAgenticPipeline/Services/IGitService.cs
- `PipelineRunner` --references--> `PipelineDbContext`  [EXTRACTED]
  .pipeline/LEMUAgenticPipeline.Web/Services/PipelineRunner.cs → .pipeline/LEMUAgenticPipeline.Web/Data/PipelineDbContext.cs
- `PipelineScheduler` --references--> `PipelineDbContext`  [EXTRACTED]
  .pipeline/LEMUAgenticPipeline.Web/Services/PipelineScheduler.cs → .pipeline/LEMUAgenticPipeline.Web/Data/PipelineDbContext.cs
- `RunInput` --references--> `ScheduleType`  [EXTRACTED]
  .pipeline/LEMUAgenticPipeline.Web/Pages/Pipeline/Run.cshtml.cs → .pipeline/LEMUAgenticPipeline.Web/Models/PipelineModels.cs

## Import Cycles
- None detected.

## Communities (124 total, 24 thin omitted)

### Community 0 - "DrawVerify.Core.Models"
Cohesion: 0.08
Nodes (13): DrawVerify.Core.Models, AuditEntry, DimensionResult, Document, DocumentVersion, DrawingDocument, GdtResult, Role (+5 more)

### Community 1 - "DrawVerify.Core.Interfaces"
Cohesion: 0.10
Nodes (11): DrawVerify.Core.Interfaces, IAuditLogger, IAuthenticationService, IDataProtectionService, IDocumentRepository, IGeometryService, INotificationService, IReportGenerator (+3 more)

### Community 2 - "DrawVerify.Application.Services"
Cohesion: 0.13
Nodes (8): DrawVerify.Application.Services, AuthenticationService, DocumentService, NotificationService, ReportService, SignatureService, VerificationService, WorkflowService

### Community 3 - "DrawVerify.Application.Verification"
Cohesion: 0.18
Nodes (6): DrawVerify.Application.Verification, DimensionComparer, GdtValidator, MissingDimensionDetector, ToleranceComparer, VerificationEngine

### Community 4 - "DrawVerify.App"
Cohesion: 0.22
Nodes (5): DrawVerify.App, Application, App, Window, MainWindow

### Community 5 - "DrawVerify.Infrastructure.Persistence"
Cohesion: 0.22
Nodes (5): DrawVerify.Infrastructure.Persistence, SqliteAuditRepository, SqliteDocumentRepository, SqliteUserRepository, SqliteWorkflowRepository

### Community 6 - "DrawVerify.Geometry.Models"
Cohesion: 0.22
Nodes (5): DrawVerify.Geometry.Models, BoundingBox, DimensionData, FeatureData, GeometryData

### Community 7 - "DrawVerify.Application.Verification.GdtRules"
Cohesion: 0.22
Nodes (5): DrawVerify.Application.Verification.GdtRules, FlatnessRule, ParallelismRule, PerpendicularityRule, PositionRule

### Community 8 - "DrawVerify.Core.Enums"
Cohesion: 0.22
Nodes (5): DrawVerify.Core.Enums, DocumentStatus, GdtControlType, VerificationStatus, WorkflowStep

### Community 9 - "DrawVerify.Core.csproj"
Cohesion: 0.18
Nodes (9): net9.0-windows, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Test.Sdk (17.8.0), xunit (2.6.6), xunit.runner.visualstudio (2.5.6) (+1 more)

### Community 10 - ".RunFromTestDesignApprovalToPrReadyAsync"
Cohesion: 0.21
Nodes (5): IOptions, ILoggerFactory, StoryFlows, Task, IGitService

### Community 11 - "OcctGeometryService.cs"
Cohesion: 0.29
Nodes (5): DrawVerify.Geometry, DrawVerify.OcctService, IGeometryService, NativeResourceTracker, OcctGeometryService

### Community 12 - "FileStateStore"
Cohesion: 0.07
Nodes (19): IHubContext, JsonSerializerOptions, DateTime, Dictionary, PipelineStateData, Task, IJiraService, string (+11 more)

### Community 13 - "OcctShapeSafeHandle"
Cohesion: 0.29
Nodes (4): DrawVerify.OcctService.SafeHandles, SafeHandleZeroOrMinusOneIsInvalid, OcctShapeSafeHandle, OcctStepReaderSafeHandle

### Community 14 - "DrawVerify.Application.Workflow"
Cohesion: 0.29
Nodes (4): DrawVerify.Application.Workflow, WorkflowEngine, WorkflowStateMachine, WorkflowTransition

### Community 15 - "PipelineRun"
Cohesion: 0.08
Nodes (34): LEMUAgenticPipeline.Web.Pages, DbContext, DbSet, PageModel, ModelBuilder, PipelineDbContext, DateTime, List (+26 more)

### Community 17 - "DrawVerify.Infrastructure.Signatures"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Signatures, SignatureStampService, X509SignatureService

### Community 18 - "DrawVerify.Infrastructure.Logging"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Logging, CentralizedLogger, LogEntry

### Community 19 - "UserControl"
Cohesion: 0.33
Nodes (4): DrawVerify.App.Controls, UserControl, SignatureStampControl, UserControl

### Community 20 - "DrawVerify.Core.Configuration"
Cohesion: 0.40
Nodes (3): DrawVerify.Core.Configuration, DocumentTemplateConfiguration, WorkflowConfiguration

### Community 21 - "DrawVerify.Infrastructure.FileStorage"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.FileStorage, DocumentFileStore, LocalFileStorageService

### Community 22 - "DrawVerify.Application.Fingerprinting"
Cohesion: 0.40
Nodes (3): DrawVerify.Application.Fingerprinting, DocumentFingerprintService, FingerprintPolicy

### Community 23 - "DrawVerify.Infrastructure.DataProtection"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.DataProtection, AesDataProtectionService, KeyProvider

### Community 24 - "DrawVerify.Infrastructure.Pdf"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Pdf, PdfDocumentBuilder, PdfReportGenerator

### Community 25 - "UserControl"
Cohesion: 0.40
Nodes (3): DocumentDetailViewModel, UserControl, DocumentDetailView

### Community 26 - "DrawVerify.App.Views"
Cohesion: 0.33
Nodes (3): DrawVerify.App.Views, UserControl, DocumentListView

### Community 27 - "StoryContext"
Cohesion: 0.07
Nodes (24): Dictionary, Task, ClaudeAgentService, List, AgentResult, AgentType, ExecutionLogEntry, ModelRoutingConfiguration (+16 more)

### Community 28 - "LEMUAgenticPipeline.Services"
Cohesion: 0.08
Nodes (25): LEMUAgenticPipeline.OpenCode, LEMUAgenticPipeline.Web.Models, LEMUAgenticPipeline.OpenAI, LEMUAgenticPipeline.Jira, LEMUAgenticPipeline.Orchestrator, LEMUAgenticPipeline.Configuration, LEMUAgenticPipeline.Examples, LEMUAgenticPipeline.State (+17 more)

### Community 29 - ".ValidateAsync"
Cohesion: 0.10
Nodes (27): AutoSelected, List, AgentDefinition, DefaultAgents, DateTime, IActionResult, List, Task (+19 more)

### Community 34 - "JiraRestService"
Cohesion: 0.16
Nodes (11): Func, HttpRequestMessage, HttpResponseMessage, JsonDocument, JsonElement, HttpClient, IEnumerable, ILogger (+3 more)

### Community 35 - "Exception"
Cohesion: 0.28
Nodes (6): DrawVerify.Core.Exceptions, Exception, JiraServiceException, DocumentNotFoundException, VerificationException, WorkflowException

### Community 36 - "AGENTS.md"
Cohesion: 0.11
Nodes (17): Agentic Pipeline Documentation, Build, Build, Run, and Publish, Clarification, Gitleaks, Graphify, Implementation Guidance, Logging and Observability (+9 more)

### Community 37 - "DrawVerify.Application.Tests"
Cohesion: 0.13
Nodes (8): DrawVerify.Application.Tests, DimensionComparerTests, FingerprintServiceTests, GdtValidatorTests, MissingDimensionDetectorTests, ToleranceComparerTests, VerificationServiceTests, WorkflowServiceTests

### Community 38 - "DrawVerify.sln"
Cohesion: 0.28
Nodes (3): Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk

### Community 39 - "Integration Tests"
Cohesion: 0.04
Nodes (47): Acceptance Criteria, Backward-Compatibility Concerns, Config Variants, Corpus (commit under `tests/TestData/Geometry/` or generate inline; synthetic fakes for unit), DTO Fixtures (FakeGeometryService canned results), Edge, Error Handling & Observability, Execution Notes for Implementer (+39 more)

### Community 40 - "SKILL.md"
Cohesion: 0.17
Nodes (11): Before Writing Code, Code Quality, Communication, Default Rule, Development Principles, Mindset, Performance, Production Safety (+3 more)

### Community 41 - "Technical Design Detail — KAN-20 US-1.1.2: Extract STEP Measurements"
Cohesion: 0.07
Nodes (26): 1. Core Domain — Models & Enums (additive, backward-compat), 2. Core Interfaces — Geometry Boundary (the ONLY OCCT seam), 3. Application Layer — Orchestration, AuthZ, Audit, Timeout, 4. Infrastructure — OCCT Isolation (THE hard boundary), 5. Persistence — SQLite, 6. App Layer — ViewModels & Views, 7. Cross-Cutting, Acceptance Criteria Mapping (+18 more)

### Community 42 - "LEMUAgenticPipeline.Orchestrator"
Cohesion: 0.20
Nodes (10): Microsoft.Extensions.DependencyInjection (9.0.0), Microsoft.Extensions.Hosting (9.0.0), Microsoft.Extensions.Logging (9.0.0), System.CommandLine (2.0.0-beta4.22272.1), System.Text.Json (9.0.0), LEMUAgenticPipeline.Orchestrator, net9.0, Microsoft.Extensions.Configuration (9.0.0) (+2 more)

### Community 43 - "PipelineWorker"
Cohesion: 0.31
Nodes (7): BackgroundService, IServiceProvider, CancellationToken, ILogger, Task, PipelineWorker, SemaphoreSlim

### Community 44 - "Migration: Move ALL pipeline code under `.pipeline/` (Phase 1 — Impact Analysis)"
Cohesion: 0.25
Nodes (7): 1. Current Architecture (after recent refactor), 2. Motivation, 3. Proposed Architecture (all pipeline code under `.pipeline/`), 4. Impact List, 5. Risks & Considerations, 6. Step-by-Step Migration Plan (Phase 2), Migration: Move ALL pipeline code under `.pipeline/` (Phase 1 — Impact Analysis)

### Community 45 - "LEMUAgenticPipeline.Web"
Cohesion: 0.29
Nodes (7): Microsoft.AspNetCore.SignalR (1.1.0), Microsoft.EntityFrameworkCore (9.0.0), Microsoft.EntityFrameworkCore.Design (9.0.0), Microsoft.EntityFrameworkCore.Sqlite (9.0.0), LEMUAgenticPipeline.Web, net9.0, Microsoft.NET.Sdk.Web

### Community 46 - "DrawVerify.Infrastructure.Tests"
Cohesion: 0.29
Nodes (4): DrawVerify.Infrastructure.Tests, DataProtectionTests, PdfReportTests, SqliteRepositoryTests

### Community 47 - "Architecture"
Cohesion: 0.29
Nodes (6): Architecture, Boundaries, Code base directional, Dependency rule, Layering, Source of truth

### Community 48 - "LEMUAgenticPipeline.csproj"
Cohesion: 0.29
Nodes (7): Microsoft.Extensions.Logging.Abstractions (9.0.0), Microsoft.Extensions.Options (9.0.0), LEMUAgenticPipeline, net9.0, Microsoft.Extensions.Configuration (9.0.0), Microsoft.Extensions.Configuration.Json (9.0.0), Microsoft.NET.Sdk

### Community 49 - "Pipeline Orchestrator"
Cohesion: 0.29
Nodes (6): Adding a new agent, Model routing (no hard-coding), Pipeline Orchestrator, Quick start, State & artifacts, Where safety limits are enforced

### Community 50 - "DrawVerify.Geometry.Tests.csproj"
Cohesion: 0.40
Nodes (4): Microsoft.NET.Test.Sdk (17.8.0), xunit (2.6.6), xunit.runner.visualstudio (2.5.6), Microsoft.NET.Sdk

### Community 51 - "IBuildTestService"
Cohesion: 0.13
Nodes (9): Task, StubBuildTestService, DotNetBuildTestService, Task, BuildResult, IBuildTestService, TestResult, DotNetBuildTestService (+1 more)

### Community 52 - "Deployment — OCCT native DLL distribution + AOT validation"
Cohesion: 0.33
Nodes (5): AOT / trimming validation, Build pipeline, Deployment — OCCT native DLL distribution + AOT validation, Runtime distribution, Version log

### Community 53 - "Fingerprint Policy"
Cohesion: 0.33
Nodes (5): Algorithm, Behavior, Fingerprint Policy, Purpose, Stages

### Community 54 - "InitialCreate"
Cohesion: 0.11
Nodes (11): LEMUAgenticPipeline.Web.Migrations, Migration, ModelSnapshot, MigrationBuilder, ModelBuilder, InitialCreate, MigrationBuilder, ModelBuilder (+3 more)

### Community 55 - "UserControl"
Cohesion: 0.33
Nodes (3): LoginViewModel, UserControl, LoginView

### Community 56 - "UserControl"
Cohesion: 0.33
Nodes (3): ReportViewModel, UserControl, ReportView

### Community 57 - "UserControl"
Cohesion: 0.33
Nodes (3): VerificationViewModel, UserControl, VerificationView

### Community 58 - "UserControl"
Cohesion: 0.33
Nodes (3): WorkflowViewModel, UserControl, WorkflowView

### Community 59 - "Engineering Rules & Known Gaps"
Cohesion: 0.40
Nodes (4): Engineering Rules & Known Gaps, How to record a new rule or gap, Known gaps / decisions to make, Recorded rules (enforced by this structure)

### Community 60 - "MVP GD&T Subset"
Cohesion: 0.40
Nodes (4): Explicitly out of scope (MVP), MVP GD&T Subset, Rule placement, Supported controls

### Community 61 - "LGPL Compliance — OCCT"
Cohesion: 0.40
Nodes (4): Contact, Distribution requirements, LGPL Compliance — OCCT, Obligations

### Community 63 - "DrawVerify"
Cohesion: 0.40
Nodes (4): Build notes, Dependency rules (enforced structurally), DrawVerify, Solution layout

### Community 64 - "UserControl"
Cohesion: 0.40
Nodes (3): DashboardViewModel, UserControl, DashboardView

### Community 65 - "DrawVerify.Core.Tests.csproj"
Cohesion: 0.40
Nodes (4): Microsoft.NET.Test.Sdk (17.8.0), xunit (2.6.6), xunit.runner.visualstudio (2.5.6), Microsoft.NET.Sdk

### Community 66 - "DrawVerify.Infrastructure.Tests.csproj"
Cohesion: 0.40
Nodes (4): Microsoft.NET.Test.Sdk (17.8.0), xunit (2.6.6), xunit.runner.visualstudio (2.5.6), Microsoft.NET.Sdk

### Community 67 - "implementation-result.schema.json"
Cohesion: 0.50
Nodes (3): $schema, title, type

### Community 68 - "review-result.schema.json"
Cohesion: 0.50
Nodes (3): $schema, title, type

### Community 69 - "technical-design.schema.json"
Cohesion: 0.50
Nodes (3): $schema, title, type

### Community 70 - "test-design.schema.json"
Cohesion: 0.50
Nodes (3): $schema, title, type

### Community 81 - "Agentic Pipeline: Current Architecture and Runtime Behavior"
Cohesion: 0.18
Nodes (10): Agentic Pipeline: Current Architecture and Runtime Behavior, Agents, prompts, and artifacts, Components, Configuration and operation, Current operational boundary, Repository structure, Safety configuration and enforcement, Scope (+2 more)

### Community 91 - "Technical Design Detail — KAN-19 US-1.1.1: Upload STEP File"
Cohesion: 0.07
Nodes (27): 1. Core Domain — Models & Enums, 2. Core Interfaces, 3. Application Layer — `src/DrawVerify.Application/Services/StepUploadService.cs` (new), 4. Infrastructure — File, Repo, Audit, Logger, 5. App Layer — WPF MVVM, Acceptance Criteria Mapping, Audit Logging (AC-10), Backward-Compat & Migration Notes (+19 more)

### Community 92 - "LEMU Agentic Pipeline — Web Control Plane"
Cohesion: 0.15
Nodes (12): Architecture, Configuration, Database, Definition of Done (MVP), Extensibility, Features (MVP), Human Approval (agent-suffixed, from JIRA-specific source artifact), LEMU Agentic Pipeline — Web Control Plane (+4 more)

### Community 93 - "http"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 94 - "DrawVerify.OcctService.Tests.csproj"
Cohesion: 0.40
Nodes (4): Microsoft.NET.Test.Sdk (17.8.0), xunit (2.6.6), xunit.runner.visualstudio (2.5.6), Microsoft.NET.Sdk

### Community 95 - "PipelineOrchestrator"
Cohesion: 0.09
Nodes (11): PipelineState, ILogger, int, Task, PipelineOrchestrator, Dictionary, HashSet, StateMachine (+3 more)

### Community 103 - "Test Design — KAN-19 US-1.1.1: Upload STEP File"
Cohesion: 0.05
Nodes (40): Acceptance Criteria, Backward-Compatibility Concerns, Config Variants, Corpus (commit under `tests/TestData/StepUpload/` or generate inline), Edge, Error Handling & Observability, Execution Notes for Implementer, In Scope for KAN-19 (+32 more)

### Community 106 - "ICodeAgentService"
Cohesion: 0.25
Nodes (6): Dictionary, List, Task, ICodeAgentService, SchemaValidationResult, IOpenCodeAgentService

### Community 122 - "Technical Design"
Cohesion: 0.11
Nodes (18): Acceptance Criteria, Acceptance Criteria Mapping, API / Interface Changes, Backward Compatibility, Database Changes, Error Handling, Existing Architecture, Existing Code (+10 more)

## Knowledge Gaps
- **395 isolated node(s):** `net9.0`, `Microsoft.Extensions.Configuration (9.0.0)`, `Microsoft.Extensions.Configuration.Json (9.0.0)`, `Microsoft.Extensions.DependencyInjection (9.0.0)`, `Microsoft.Extensions.Hosting (9.0.0)` (+390 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **24 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PipelineOrchestrator` connect `PipelineOrchestrator` to `ICodeAgentService`, `.RunFromTestDesignApprovalToPrReadyAsync`, `FileStateStore`, `IBuildTestService`, `StoryContext`, `LEMUAgenticPipeline.Services`?**
  _High betweenness centrality (0.029) - this node is a cross-community bridge._
- **Why does `IJiraService` connect `FileStateStore` to `JiraRestService`, `.RunFromTestDesignApprovalToPrReadyAsync`, `PipelineRun`, `LEMUAgenticPipeline.Services`, `.ValidateAsync`, `PipelineOrchestrator`?**
  _High betweenness centrality (0.026) - this node is a cross-community bridge._
- **Why does `FileStateStore` connect `FileStateStore` to `.RunFromTestDesignApprovalToPrReadyAsync`, `PipelineRun`, `StoryContext`, `LEMUAgenticPipeline.Services`, `PipelineOrchestrator`?**
  _High betweenness centrality (0.023) - this node is a cross-community bridge._
- **What connects `net9.0`, `Microsoft.Extensions.Configuration (9.0.0)`, `Microsoft.Extensions.Configuration.Json (9.0.0)` to the rest of the system?**
  _395 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `DrawVerify.Core.Models` be split into smaller, more focused modules?**
  _Cohesion score 0.08 - nodes in this community are weakly interconnected._
- **Should `DrawVerify.Core.Interfaces` be split into smaller, more focused modules?**
  _Cohesion score 0.09523809523809523 - nodes in this community are weakly interconnected._
- **Should `DrawVerify.Application.Services` be split into smaller, more focused modules?**
  _Cohesion score 0.13333333333333333 - nodes in this community are weakly interconnected._