# DrawVerify

A drawing-verification desktop application for engineering documents: versioned drawing
management, dimension and GD&T verification against a tolerance/geometry model, workflow
state tracking, and digitally-signed verification reports.

## Solution layout

```
DrawVerify.sln
├── src/
│   ├── DrawVerify.App             (WPF shell — views, thin view models, DI composition root)
│   ├── DrawVerify.Core            (domain models, contracts, enums — no I/O, no OCCT)
│   ├── DrawVerify.Application     (business logic / orchestration — references Core only)
│   ├── DrawVerify.Infrastructure  (I/O: DB, files, PDF, signatures, notifications)
│   ├── DrawVerify.Geometry        (pure geometry abstraction — the hard OCCT boundary)
│   ├── DrawVerify.OcctService     (adapter implementing IGeometryService via SWIG wrapper)
│   └── DrawVerify.OcctWrapper     (SWIG-generated C# wrapper — generated code, do not edit)
├── native/                        (OCCT native build: CMake, SWIG, deployment scripts)
├── tests/                         (unit tests + integration tests)
├── config/                        (workflow, templates, GD&T subset, fingerprint policy)
└── docs/                          (architecture, deployment, LGPL compliance, engineering rules)
```

## Dependency rules (enforced structurally)

1. Dependencies point down only: App → Application → Core; App → Geometry (interface only);
   OcctService → Geometry + OcctWrapper; OcctWrapper → OCCT native DLLs.
2. No OCCT types appear in Core, Application, Geometry interfaces, or App.
3. No business logic in WPF Views or code-behind — thin ViewModels only.
4. All I/O lives in Infrastructure, accessed through Core interfaces.
5. Core verification logic is testable without files, DB, or WPF (mock/fake geometry service).
6. Encryption lives in Infrastructure/DataProtection; keys are never in source or config.

See `docs/architecture.md` and `docs/engineering-rules.md` for the full record.

## Build notes

- The `DrawVerify.App` WPF project targets `net9.0-windows` and requires a Windows build host.
- OCCT native DLLs are built via `native/scripts/build-occt.ps1` and the C# wrapper is
  regenerated via `native/scripts/generate-wrapper.ps1` (SWIG output lands in
  `src/DrawVerify.OcctWrapper/Generated`). See `docs/deployment.md`.
