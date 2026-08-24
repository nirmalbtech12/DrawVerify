# Graph Report - DrawVerify  (2026-08-24)

## Corpus Check
- 154 files · ~10,955 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 411 nodes · 398 edges · 47 communities (38 shown, 9 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 7 edges (avg confidence: 0.5)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `6ce09d8e`
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
- DrawVerify.sln
- OcctGeometryService.cs
- OcctShapeSafeHandle
- DrawVerify.Application.Workflow
- DrawVerify.Infrastructure.Signatures
- DrawVerify.Infrastructure.Logging
- SignatureStampControl
- DrawVerify.Core.Configuration
- DrawVerify.Infrastructure.FileStorage
- DrawVerify.Application.Fingerprinting
- DrawVerify.Infrastructure.DataProtection
- DrawVerify.Infrastructure.Pdf
- UserControl
- InAppNotificationService.cs
- OcctToGeometryMapper.cs
- CompositionRoot.cs
- GeometryType.cs
- DrawVerify.Core.Exceptions
- AGENTS.md
- DrawVerify.Application.Tests
- SKILL.md
- DrawVerify.Infrastructure.Tests
- Architecture
- Deployment — OCCT native DLL distribution + AOT validation
- Fingerprint Policy
- Engineering Rules & Known Gaps
- MVP GD&T Subset
- LGPL Compliance — OCCT
- graphify.js
- DrawVerify
- OcctGeometryServiceTests.cs
- MockGeometryServiceTests.cs
- DrawVerify.OcctWrapper
- Agentic Pipeline: Current Architecture and Runtime Behavior
- .opencode/README.md

## God Nodes (most connected - your core abstractions)
1. `DrawVerify.Core.Models` - 12 edges
2. `DrawVerify.Core.Interfaces` - 10 edges
3. `Agentic Pipeline: Current Architecture and Runtime Behavior` - 10 edges
4. `DrawVerify.App.ViewModels` - 7 edges
5. `DrawVerify.App.Views` - 7 edges
6. `DrawVerify.Application.Services` - 7 edges
7. `DrawVerify.Application.Tests` - 7 edges
8. `Architecture` - 6 edges
9. `DrawVerify.Application.Verification` - 5 edges
10. `Build, Run, and Publish` - 5 edges

## Surprising Connections (you probably didn't know these)
- `UserControl` --references--> `DashboardViewModel`  [INFERRED]
  src/DrawVerify.App/Views/DashboardView.xaml → src/DrawVerify.App/ViewModels/DashboardViewModel.cs
- `UserControl` --references--> `DocumentDetailViewModel`  [INFERRED]
  src/DrawVerify.App/Views/DocumentDetailView.xaml → src/DrawVerify.App/ViewModels/DocumentDetailViewModel.cs
- `UserControl` --references--> `DocumentListViewModel`  [INFERRED]
  src/DrawVerify.App/Views/DocumentListView.xaml → src/DrawVerify.App/ViewModels/DocumentListViewModel.cs
- `UserControl` --references--> `LoginViewModel`  [INFERRED]
  src/DrawVerify.App/Views/LoginView.xaml → src/DrawVerify.App/ViewModels/LoginViewModel.cs
- `UserControl` --references--> `ReportViewModel`  [INFERRED]
  src/DrawVerify.App/Views/ReportView.xaml → src/DrawVerify.App/ViewModels/ReportViewModel.cs

## Import Cycles
- None detected.

## Communities (47 total, 9 thin omitted)

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

### Community 9 - "DrawVerify.sln"
Cohesion: 0.07
Nodes (28): net9.0-windows, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk (+20 more)

### Community 11 - "OcctGeometryService.cs"
Cohesion: 0.29
Nodes (5): DrawVerify.Geometry, DrawVerify.OcctService, IGeometryService, NativeResourceTracker, OcctGeometryService

### Community 13 - "OcctShapeSafeHandle"
Cohesion: 0.29
Nodes (4): DrawVerify.OcctService.SafeHandles, SafeHandleZeroOrMinusOneIsInvalid, OcctShapeSafeHandle, OcctStepReaderSafeHandle

### Community 14 - "DrawVerify.Application.Workflow"
Cohesion: 0.29
Nodes (4): DrawVerify.Application.Workflow, WorkflowEngine, WorkflowStateMachine, WorkflowTransition

### Community 17 - "DrawVerify.Infrastructure.Signatures"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Signatures, SignatureStampService, X509SignatureService

### Community 18 - "DrawVerify.Infrastructure.Logging"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Logging, CentralizedLogger, LogEntry

### Community 19 - "SignatureStampControl"
Cohesion: 0.40
Nodes (3): DrawVerify.App.Controls, UserControl, SignatureStampControl

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
Cohesion: 0.06
Nodes (24): DrawVerify.App.Views, DrawVerify.App.ViewModels, DashboardViewModel, DocumentDetailViewModel, DocumentListViewModel, LoginViewModel, ReportViewModel, VerificationViewModel (+16 more)

### Community 35 - "DrawVerify.Core.Exceptions"
Cohesion: 0.32
Nodes (5): DrawVerify.Core.Exceptions, Exception, DocumentNotFoundException, VerificationException, WorkflowException

### Community 36 - "AGENTS.md"
Cohesion: 0.11
Nodes (17): Agentic Pipeline, Build, Build, Run, and Publish, Clarification, Gitleaks, Graphify, Implementation Guidance, Logging and Observability (+9 more)

### Community 37 - "DrawVerify.Application.Tests"
Cohesion: 0.13
Nodes (8): DrawVerify.Application.Tests, DimensionComparerTests, FingerprintServiceTests, GdtValidatorTests, MissingDimensionDetectorTests, ToleranceComparerTests, VerificationServiceTests, WorkflowServiceTests

### Community 40 - "SKILL.md"
Cohesion: 0.17
Nodes (11): Before Writing Code, Code Quality, Communication, Default Rule, Development Principles, Mindset, Performance, Production Safety (+3 more)

### Community 46 - "DrawVerify.Infrastructure.Tests"
Cohesion: 0.29
Nodes (4): DrawVerify.Infrastructure.Tests, DataProtectionTests, PdfReportTests, SqliteRepositoryTests

### Community 47 - "Architecture"
Cohesion: 0.29
Nodes (6): Architecture, Boundaries, Code base directional, Dependency rule, Layering, Source of truth

### Community 52 - "Deployment — OCCT native DLL distribution + AOT validation"
Cohesion: 0.33
Nodes (5): AOT / trimming validation, Build pipeline, Deployment — OCCT native DLL distribution + AOT validation, Runtime distribution, Version log

### Community 53 - "Fingerprint Policy"
Cohesion: 0.33
Nodes (5): Algorithm, Behavior, Fingerprint Policy, Purpose, Stages

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

### Community 81 - "Agentic Pipeline: Current Architecture and Runtime Behavior"
Cohesion: 0.17
Nodes (11): Agentic Pipeline: Current Architecture and Runtime Behavior, Agents, prompts, and artifacts, Components, Configuration and operation, Current operational boundary, Git Branching Strategy (per-run isolation), Repository structure, Safety configuration and enforcement (+3 more)

## Knowledge Gaps
- **180 isolated node(s):** `DrawVerify.App.Controls`, `net9.0-windows`, `Microsoft.NET.Sdk`, `DrawVerify.App.Services`, `CompositionRoot` (+175 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **9 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `SignatureStampControl` connect `SignatureStampControl` to `UserControl`?**
  _High betweenness centrality (0.002) - this node is a cross-community bridge._
- **What connects `DrawVerify.App.Controls`, `net9.0-windows`, `Microsoft.NET.Sdk` to the rest of the system?**
  _180 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `DrawVerify.Core.Models` be split into smaller, more focused modules?**
  _Cohesion score 0.08 - nodes in this community are weakly interconnected._
- **Should `DrawVerify.Core.Interfaces` be split into smaller, more focused modules?**
  _Cohesion score 0.09523809523809523 - nodes in this community are weakly interconnected._
- **Should `DrawVerify.Application.Services` be split into smaller, more focused modules?**
  _Cohesion score 0.13333333333333333 - nodes in this community are weakly interconnected._
- **Should `DrawVerify.sln` be split into smaller, more focused modules?**
  _Cohesion score 0.06533776301218161 - nodes in this community are weakly interconnected._
- **Should `UserControl` be split into smaller, more focused modules?**
  _Cohesion score 0.05656565656565657 - nodes in this community are weakly interconnected._