# Deployment — OCCT native DLL distribution + AOT validation

## Build pipeline

1. Obtain OCCT source → `native/occt` (git submodule or vendored), record the exact
   version here and in `src/DrawVerify.OcctWrapper/README.md`.
2. Build native DLLs → `native/scripts/build-occt.ps1` (CMake, output to `native/bin`).
   Required toolkits at MVP: `TKernel.dll`, `TKBRep.dll`, `TKXDESTEP.dll`, `TKXSBase.dll`.
3. Generate C# wrapper → `native/scripts/generate-wrapper.ps1` (SWIG against
   `native/swig/occcsharp.i`), output to `src/DrawVerify.OcctWrapper/Generated`.

## Runtime distribution

- `TK*.dll` are copied beside the managed assemblies on publish.
- Native handle lifetime is managed via `SafeHandle` derivations in
  `DrawVerify.OcctService.SafeHandles`; the `NativeResourceTracker` enables leak
  detection in debug builds only.
- DLL loading is validated by `native/scripts/validate-deployment.ps1` (deployment spike)
  and exercised by `tests/DrawVerify.OcctService.Tests` (integration, requires native/bin).

## AOT / trimming validation

- The OCCT native surface is the only native dependency; the managed side is kept
  reflection-free where possible to stay compatible with trimming.
- AOT/trimming validation must be re-run whenever the SWIG wrapper is regenerated.

## Version log

| OCCT version | SWIG version | Validated | Notes |
|--------------|--------------|-----------|-------|
| (TBD)        | (TBD)        | (TBD)     | Record pinned versions here |