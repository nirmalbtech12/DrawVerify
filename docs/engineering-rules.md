# Engineering Rules & Known Gaps

## Recorded rules (enforced by this structure)

1. Dependencies point DOWN only: App → Application → Core; App → Geometry (interface only);
   OcctService → Geometry + OcctWrapper; OcctWrapper → OCCT native DLLs.
2. No OCCT types in Core, Application, Geometry interface, or App. Only
   `DrawVerify.OcctService` touches OCCT/SWIG types.
3. No business logic in WPF Views or code-behind — thin ViewModels only.
4. All I/O (DB, files, PDF, signatures, notifications, native libs) lives in
   Infrastructure and is reached through Core interfaces.
5. Core verification logic is testable without physical files, DB, or WPF UI.
   Tests use a fake `IGeometryService` (see `DrawVerify.Geometry.Tests`).
6. Encryption lives in Infrastructure/DataProtection; keys are never stored in
   source code or config (see `KeyProvider`).
7. Workflow rules and document templates load from `config/` — never hard-coded.
8. Native resource lifetime uses `SafeHandle`; `NativeResourceTracker` exists for debug builds.
9. SWIG-generated code lives only in `DrawVerify.OcctWrapper/Generated` and is never
   hand-edited; `.cxx` glue is compiled into the native build.
10. The MVP GD&T subset and fingerprint policy are documented in `docs/` and `config/`
    BEFORE implementation.

## Known gaps / decisions to make

- [ ] Pin OCCT and SWIG versions (see `docs/deployment.md` version log).
- [ ] Decide SQLite provider (Microsoft.Data.Sqlite vs EF Core) for Infrastructure.
- [ ] Decide PDF library (QuestPDF/iTextSharp.Net) for `PdfReportGenerator`.
- [ ] Define the concrete `IGeometryService` method signatures (LoadStep, GetVolume,
      GetBoundingBox, feature queries) in the Geometry boundary.
- [ ] Confirm WPF MVVM framework choice (CommunityToolkit.Mvvm etc.) once UI starts.
- [ ] Decide auth credential store (password hash + salt, key derivation parameters).
- [ ] Agree CI strategy: lightweight (no OCCT) vs integration (requires native/bin).

## How to record a new rule or gap

Append to this file when a structural decision is made; update `docs/architecture.md`
when the dependency map changes.