# DrawVerify.OcctWrapper

SWIG-generated C# wrapper around Open CASCADE Technology (OCCT).

This project is **generated code**. Files under `Generated/` are produced by SWIG and
must **never be hand-edited**. To regenerate:

```powershell
native/scripts/generate-wrapper.ps1
```

- SWIG interface: `native/swig/occcsharp.i`
- OCCT version: pin the exact OCCT version in `native/occt` and record it in
  `docs/deployment.md` alongside this file.
- Native DLLs are built from `native/occt` via `native/scripts/build-occt.ps1` and
  deployed beside the app per `docs/deployment.md`.

## Contents

- `Generated/*.cs` — generated C# wrapper classes (`dotnet build` generates these when
  they are absent or stale)
- `Generated/*.cxx` — generated C++ glue compiled into the OCCT native build
