# OCCT native integration

DrawVerify uses OCCT **8.0.0**, pinned at the `V8_0_0` tag in `native/occt`.
Initialize it with `git submodule update --init --recursive`; do not substitute
master or a patch release. The source is unmodified. Retain that exact source
with each release as the corresponding source for the shipped shared libraries.

```
DrawVerify
  ↓
DrawVerify.Occt.dll
  ↓
DrawVerify.OcctWrapper (SWIG generated interop)
  ↓
DrawVerifyOcctNative
  ↓
OCCT shared libraries
```

The native façade owns `TopoDS_Shape`; no OCCT type is public in DrawVerify's
managed API. Models are disposed deterministically. OCCT failures are converted
to `OperationResult` at the façade boundary.

The coordinate convention is right-handed: X right, Y up, Z out of the front
projection plane. OCCT extracts the STEP transfer length-unit scale and exposes
it in millimetres with the loaded model. The BRep remains in OCCT's transfer
coordinates; consumers use the metadata when normalizing comparison data.

Run `native/scripts/build-occt.ps1`, `generate-wrapper.ps1`, then
`build-native.ps1`. Its staging step writes the native bridge and required OCCT
shared libraries to `native/out/runtime/<rid>/native`; package that directory
beside the application (or under its `runtimes/<rid>/native` directory).
`DrawVerify.OcctWrapper` resolves only those explicit deployment locations and
does not depend on a developer-machine PATH.

Run `native/scripts/verify-native.ps1` for the authoritative check. It builds
the native/managed chain, runs the packaged-layout CTest smoke, then runs the
real `DrawVerify.OcctService.Tests` xUnit integration suite. Missing native
libraries or STEP fixtures are build errors; required tests are never skipped.
`preflight-native.ps1` emits `READY` only after source/version/tooling/build,
native runtime, fixture, CTest, and xUnit checks have passed; failures use
`ENVIRONMENT_MISSING`, `BUILD_FAILED`, `NATIVE_LOAD_FAILED`,
`STEP_READ_FAILED`, or `PROJECTION_FAILED` categories.

OCCT is LGPL-2.1 with the OCCT LGPL Exception. Distribute
`LICENSE_LGPL_21.txt`, `OCCT_LGPL_EXCEPTION.txt`, and `THIRD-PARTY-NOTICES.txt`.
Do not statically link OCCT or prohibit reverse engineering where LGPL rights
require debugging modifications. XCAF, PMI, GD&T, assemblies, colours, and DXF
are intentionally outside this foundation.

The foundational API is read-only after load. It uses operation-local OCCT
readers and algorithms; concurrent access has not been certified and callers
must synchronize access to a loaded model. The fixture generator produces cube,
cylinder, through-hole plate, and stepped-block STEP models during the native
build. The generated fixture source is deterministic and remains part of the
exact native build inputs retained for a release's corresponding-source record.

Projection output is normalized DrawVerify-owned `Line2`, `Arc2`, `Circle2`,
and `Polyline2` data. A future DXF parser must produce equivalent DTOs; the
comparison layer must not reference OCCT. Edge-to-edge and face-to-face
measurements are deliberately unavailable until their unambiguous engineering
semantics are specified.
