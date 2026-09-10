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

The canonical [GD&T support and capability contract](docs/gdt-subset.md)
documents the 14-control target scope, standards alignment, current
implementation baseline, and known extraction/verification limitations.

## Build notes

- The `DrawVerify.App` WPF project targets `net9.0-windows` and requires a Windows build host.
- OCCT native DLLs are built via `native/scripts/build-occt.ps1` and the C# wrapper is
  regenerated via `native/scripts/generate-wrapper.ps1` (SWIG output lands in
  `src/DrawVerify.OcctWrapper/Generated`). See `docs/deployment.md`.

## Run on Windows

DrawVerify is a Windows WPF application. Run these commands from the repository
root in PowerShell.

### Prerequisites

Install:

- .NET 9 SDK
- Visual Studio 2022 with Desktop development with C++ and a Windows SDK
- CMake
- SWIG 4.x
- Git

### Initialize OCCT

OCCT is pinned as a Git submodule. If the PowerShell Git wrapper cannot find
Git's Unix helper tools, initialize it through Git Bash:

```powershell
$repo = (Get-Location).Path.Replace('\', '/')
& 'C:\Program Files\Git\bin\bash.exe' -lc "cd '$repo' && git submodule update --init --recursive"
```

Verify that `native/occt/CMakeLists.txt` exists before continuing.

### Build the native pipeline

```powershell
.\native\scripts\build-native.ps1 -Configuration Release -ParallelJobs 2
```

This builds and installs OCCT, generates the SWIG C# and C++ wrappers, builds
`DrawVerifyOcctNative.dll`, generates native fixtures, and stages the Windows
runtime under `native/out/runtime/win-x64/native`.

### Verify the native pipeline

```powershell
.\native\scripts\preflight-native.ps1 -Configuration Release -RunTests
```

The command must finish with `READY`. It validates the native bridge, OCCT
runtime DLLs, fixtures, CTest smoke tests, and managed OCCT integration tests.

### Publish and launch the application

```powershell
.\run.ps1 -Configuration Release
```

`run.ps1` restores and publishes the self-contained `win-x64` WPF application,
copies the native OCCT runtime beside it, and launches `DrawVerify.App.exe`.
The publish destination is read from `src/DrawVerify.App/appsettings.json`
under `Publish.Path`.

The Git Bash compatibility entry point is also available:

```bash
./run.sh
```

After the initial native build, repeat the native build and preflight commands
when native or wrapper inputs change. For managed-only changes, rerun
`run.ps1` to republish and launch the application.
