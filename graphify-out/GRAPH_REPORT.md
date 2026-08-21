# Graph Report - /home/nirmal/projects/DrawVerify  (2026-08-20)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 290 nodes · 253 edges · 70 communities (64 shown, 6 thin omitted)
- Extraction: 97% EXTRACTED · 3% INFERRED · 0% AMBIGUOUS · INFERRED: 7 edges (avg confidence: 0.5)
- Token cost: 0 input · 0 output

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
- Microsoft.NET.Sdk
- DrawVerify.Core.Exceptions
- OcctGeometryService.cs
- OcctShapeSafeHandle
- UserControl
- DrawVerify.Application.Workflow
- UserControl
- DrawVerify.App.Views
- UserControl
- UserControl
- DrawVerify.Infrastructure.Signatures
- DrawVerify.Infrastructure.Logging
- DrawVerify.Core.Configuration
- DrawVerify.Infrastructure.FileStorage
- DrawVerify.Application.Fingerprinting
- DrawVerify.Infrastructure.DataProtection
- DrawVerify.Infrastructure.Pdf
- UserControl
- UserControl
- UserControl
- DrawVerify.App.ViewModels
- InAppNotificationService.cs
- OcctToGeometryMapper.cs
- CompositionRoot.cs
- GeometryType.cs

## God Nodes (most connected - your core abstractions)
1. `DrawVerify.Core.Models` - 12 edges
2. `DrawVerify.Core.Interfaces` - 10 edges
3. `DrawVerify.App.Views` - 7 edges
4. `DrawVerify.Application.Services` - 7 edges
5. `DrawVerify.App.ViewModels` - 7 edges
6. `Microsoft.NET.Sdk` - 7 edges
7. `DrawVerify.Application.Verification` - 5 edges
8. `DrawVerify.Infrastructure.Persistence` - 4 edges
9. `DrawVerify.Geometry.Models` - 4 edges
10. `DrawVerify.Application.Verification.GdtRules` - 4 edges

## Surprising Connections (you probably didn't know these)
- `OcctGeometryService` --implements--> `IGeometryService`  [EXTRACTED]
  DrawVerify.OcctService/OcctGeometryService.cs → DrawVerify.Geometry/IGeometryService.cs
- `UserControl` --references--> `DocumentListViewModel`  [INFERRED]
  DrawVerify.App/Views/DocumentListView.xaml → DrawVerify.App/ViewModels/DocumentListViewModel.cs
- `UserControl` --references--> `VerificationViewModel`  [INFERRED]
  DrawVerify.App/Views/VerificationView.xaml → DrawVerify.App/ViewModels/VerificationViewModel.cs
- `UserControl` --references--> `WorkflowViewModel`  [INFERRED]
  DrawVerify.App/Views/WorkflowView.xaml → DrawVerify.App/ViewModels/WorkflowViewModel.cs
- `UserControl` --references--> `DashboardViewModel`  [INFERRED]
  DrawVerify.App/Views/DashboardView.xaml → DrawVerify.App/ViewModels/DashboardViewModel.cs

## Import Cycles
- None detected.

## Communities (70 total, 6 thin omitted)

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

### Community 10 - "DrawVerify.Core.Exceptions"
Cohesion: 0.32
Nodes (5): DrawVerify.Core.Exceptions, DocumentNotFoundException, VerificationException, WorkflowException, Exception

### Community 11 - "OcctGeometryService.cs"
Cohesion: 0.29
Nodes (5): DrawVerify.Geometry, DrawVerify.OcctService, IGeometryService, NativeResourceTracker, OcctGeometryService

### Community 12 - "OcctShapeSafeHandle"
Cohesion: 0.29
Nodes (4): DrawVerify.OcctService.SafeHandles, OcctShapeSafeHandle, OcctStepReaderSafeHandle, SafeHandleZeroOrMinusOneIsInvalid

### Community 13 - "UserControl"
Cohesion: 0.40
Nodes (3): DocumentDetailViewModel, UserControl, DocumentDetailView

### Community 14 - "DrawVerify.Application.Workflow"
Cohesion: 0.29
Nodes (4): DrawVerify.Application.Workflow, WorkflowEngine, WorkflowStateMachine, WorkflowTransition

### Community 15 - "UserControl"
Cohesion: 0.33
Nodes (4): DrawVerify.App.Controls, UserControl, SignatureStampControl, UserControl

### Community 16 - "DrawVerify.App.Views"
Cohesion: 0.33
Nodes (3): DrawVerify.App.Views, UserControl, DocumentListView

### Community 17 - "UserControl"
Cohesion: 0.33
Nodes (3): VerificationViewModel, UserControl, VerificationView

### Community 18 - "UserControl"
Cohesion: 0.33
Nodes (3): WorkflowViewModel, UserControl, WorkflowView

### Community 19 - "DrawVerify.Infrastructure.Signatures"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Signatures, SignatureStampService, X509SignatureService

### Community 20 - "DrawVerify.Infrastructure.Logging"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Logging, CentralizedLogger, LogEntry

### Community 21 - "DrawVerify.Core.Configuration"
Cohesion: 0.40
Nodes (3): DrawVerify.Core.Configuration, DocumentTemplateConfiguration, WorkflowConfiguration

### Community 22 - "DrawVerify.Infrastructure.FileStorage"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.FileStorage, DocumentFileStore, LocalFileStorageService

### Community 23 - "DrawVerify.Application.Fingerprinting"
Cohesion: 0.40
Nodes (3): DrawVerify.Application.Fingerprinting, DocumentFingerprintService, FingerprintPolicy

### Community 24 - "DrawVerify.Infrastructure.DataProtection"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.DataProtection, AesDataProtectionService, KeyProvider

### Community 25 - "DrawVerify.Infrastructure.Pdf"
Cohesion: 0.40
Nodes (3): DrawVerify.Infrastructure.Pdf, PdfDocumentBuilder, PdfReportGenerator

### Community 26 - "UserControl"
Cohesion: 0.40
Nodes (3): DashboardViewModel, UserControl, DashboardView

### Community 27 - "UserControl"
Cohesion: 0.33
Nodes (3): LoginViewModel, UserControl, LoginView

### Community 28 - "UserControl"
Cohesion: 0.33
Nodes (3): ReportViewModel, UserControl, ReportView

## Knowledge Gaps
- **78 isolated node(s):** `AuditEntry`, `DimensionResult`, `Document`, `DocumentVersion`, `DrawingDocument` (+73 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **6 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `DrawVerify.App.ViewModels` connect `DrawVerify.App.ViewModels` to `UserControl`, `UserControl`, `UserControl`, `UserControl`, `UserControl`?**
  _High betweenness centrality (0.006) - this node is a cross-community bridge._
- **What connects `AuditEntry`, `DimensionResult`, `Document` to the rest of the system?**
  _78 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `DrawVerify.Core.Models` be split into smaller, more focused modules?**
  _Cohesion score 0.08 - nodes in this community are weakly interconnected._
- **Should `DrawVerify.Core.Interfaces` be split into smaller, more focused modules?**
  _Cohesion score 0.09523809523809523 - nodes in this community are weakly interconnected._
- **Should `DrawVerify.Application.Services` be split into smaller, more focused modules?**
  _Cohesion score 0.13333333333333333 - nodes in this community are weakly interconnected._