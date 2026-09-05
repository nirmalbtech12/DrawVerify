# Architecture

## Layering

```
┌──────────────────────────── DrawVerify.App (WPF) ────────────────────────────┐
│ Views (UI only)  →  ViewModels (thin)  →  CompositionRoot (DI wiring)       │
└──────────────┬──────────────────────────────────────────────┬───────────────┘
               │                                              │ (interface only)
               ▼                                              ▼
┌─────────────────────────── DrawVerify.Application ────┐   ┌───────────────┐
│ Verification · Workflow · Fingerprinting · Services    │   │ DrawVerify.   │
│                                                       │   │ Geometry       │
└──────────────┬────────────────────────────────────────┘   └───────┬───────┘
               │                                                     │
               ▼                                                     ▼
┌─────────────────────────── DrawVerify.Core ────────────────────┐  ┌───────────────┐
│ Models · Enums · Interfaces · Configuration · Exceptions      │  │ DrawVerify.   │
│ (no I/O, no OCCT)                                             │  │ OcctService   │
└──────────────┬───────────────────────────┬────────────────────┘  └───────┬───────┘
               │                           │                                │
               ▼                           ▼                                ▼
┌─────────────────────── DrawVerify.Infrastructure (DTOs out)   ┌───────────────┐
│ Persistence · DataProtection · FileStorage · Pdf · Signatures │ DrawVerify.   │
└───────────────────────────────────────────────────────────────┘ OcctWrapper   │
                                                                 └───────┬───────┘
                                                                         ▼
                                                                   OCCT native DLLs
```

## Dependency rule

Dependencies point DOWN only:

- App → Application → Core
- App → Geometry (interface only)
- Infrastructure → Core + Application
- OcctService → Geometry + OcctWrapper
- OcctWrapper → OCCT native DLLs

## Boundaries

- Core, Application, the Geometry interface, and App never see OCCT types.
  Only `DrawVerify.OcctService` touches OCCT/SWIG.
- All I/O (DB, files, PDF, signatures, notifications, native libs) lives in
  Infrastructure and is consumed through Core interfaces.
- Workflow rules and document templates load from `config/` — never hard-coded.

## Test project boundaries and platform eligibility

Test projects follow the layer they exercise:

- `tests/DrawVerify.Application.Tests` tests `DrawVerify.Application` and
  targets `net9.0`. It must not reference the WPF App project and is eligible
  to run on Linux, macOS, and Windows.
- `tests/DrawVerify.App.Tests` tests `DrawVerify.App` ViewModels and other WPF
  presentation code. It targets `net9.0-windows` and is Windows-only.
- Core, Geometry, OCCT service, and Infrastructure tests remain in their
  corresponding test projects and are cross-platform unless their project
  dependencies explicitly require otherwise.

The pipeline must select test projects according to the runner operating
system. Non-Windows runners run the cross-platform test projects and visibly
skip `DrawVerify.App.Tests`; Windows runners run the complete test set,
including `DrawVerify.App.Tests`. A Windows-only test must not be included in
a cross-platform test project merely because the production Application layer
is cross-platform.

## Source of truth

This document summarizes the boundaries; the structure is enforced by project
references. See `engineering-rules.md` for recorded rules and known gaps.

## Code base directional
Review the generated .csproj files to confirm references point down only

Confirm DrawVerify.Geometry has no reference to DrawVerify.OcctWrapper or OCCT

Confirm DrawVerify.App references Core + Application + Geometry (interface), not OcctService directly (wire OcctService via DI in the composition root)
