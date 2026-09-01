# DrawVerify OCCT integration checklist

This checklist records the implementation and verification state for the 44-point OCCT foundation. Native and managed checks below run against the pinned OCCT `V8_0_0` source and shared libraries.

| # | Area | Status / evidence |
|---:|---|---|
| 1 | Repository inspection | Complete; existing projects and native layout retained. |
| 2 | OCCT pin | Complete; `native/occt` is tag `V8_0_0` (8.0.0). |
| 3 | Licensing | Complete; shared build, license files, notices and compliance documentation present. |
| 4 | Façade architecture | Complete; DrawVerify-owned C++ façade is the only SWIG input. |
| 5 | Service decomposition | Complete; reader, model, topology, geometry, validation, measurement, projection, mesh and runtime services are separate. |
| 6 | Runtime capabilities | Complete; version, capability and linkage readiness API. |
| 7 | STEP loading | Complete; validated path, extension, read, transfer and non-null shape. |
| 8 | Model lifetime | Complete; deterministic managed/native disposal and RAII. |
| 9 | DTO boundary | Complete; POD/value DTOs and enums only. |
| 10 | Model summary | Complete; topology, bounds, properties, units and validity. |
| 11 | Vertex extraction | Complete; wrapper-local stable IDs and coordinates. |
| 12 | Edge extraction | Complete; analytical metadata and `Other` fallback. |
| 13 | Face extraction | Complete; plane/cylinder/cone/sphere/torus metadata and `Other`. |
| 14 | Topology relationships | Complete; materialized face-edge, edge-vertex and solid-face IDs. |
| 15 | Bounding boxes | Complete; model/face/edge APIs with OCCT tolerance behavior documented. |
| 16 | Measurements | Complete; lengths, areas, volume, distance and vertex measurements. |
| 17 | Validation | Complete; structured status and diagnostics, no implicit repair. |
| 18 | Projection | Complete; six standard views and validated custom direction/up vectors. |
| 19 | Coordinates | Complete; documented right-handed XYZ and projection-plane convention. |
| 20 | Units | Complete; STEP unit detection and millimetre scale metadata. |
| 21 | Mesh | Complete; configurable deflection, triangles, normals and face association. |
| 22 | Error boundary | Complete; native exceptions translated to result/error model. |
| 23 | Logging context | Complete at façade diagnostics boundary; host logging sink remains application-owned. |
| 24 | SWIG interface | Complete; deliberate interface and tested vector/string/value typemaps. |
| 25 | Generated managed layer | Complete; generated code isolated and consumed internally. |
| 26 | Public managed API | Complete; `DrawVerify.Occt` loader/model/runtime contracts. |
| 27 | Native resolution | Complete; deterministic resolver and staged runtime layout. |
| 28 | CMake | Complete; C++17, shared OCCT, required toolkits and CTest smoke target. |
| 29 | XCAF/PMI | Complete for MVP scope; extension point documented, not a dependency. |
| 30 | DXF boundary | Complete; normalized projection DTOs remain independent of DXF parsing. |
| 31 | Wrapper generation | Complete; deterministic PowerShell generation script with preflight checks. |
| 32 | Build scripts | Complete; OCCT, wrapper, native, staging and verification scripts. |
| 33 | Preflight | Complete; categorized prerequisite/build/load/STEP/projection checks. |
| 34 | Native fixtures | Complete; deterministic cube, cylinder, plate-hole and stepped-block generator plus screw fixture. |
| 35 | Native tests | Complete; CTest managed smoke test passes. |
| 36 | Managed integration tests | Complete; 10 real xUnit tests pass through SWIG and OCCT. |
| 37 | Resource safety | Complete; copied DTOs, RAII and deterministic `IDisposable`. |
| 38 | Thread safety | Complete; operation-local algorithms and documented model ownership limitation. |
| 39 | Performance | Complete for foundation; one STEP load per model and no global mutable readers/caches. |
| 40 | Backward compatibility | Complete; legacy geometry service now delegates to the public OCCT loader. |
| 41 | Documentation | Complete; architecture, deployment, units, limitations and licensing docs. |
| 42 | Full build | Complete; `dotnet build DrawVerify.sln --no-restore -m:1` succeeds. |
| 43 | Full test evidence | Complete; OCCT xUnit 10/10 and CTest 1/1 pass. Other solution test projects currently contain no discoverable tests. |
| 44 | Definition of done | Complete for the validated Linux native/managed chain. Windows packaging/publish must still be exercised on a Windows runner before release sign-off. |

## Verification commands

```text
dotnet build DrawVerify.sln --no-restore -m:1
dotnet test tests/DrawVerify.OcctService.Tests/DrawVerify.OcctService.Tests.csproj --no-restore -m:1
ctest --test-dir native/build/drawverify-occt --output-on-failure
```

The exact OCCT source and corresponding shared-library build are retained under `native/occt` and `native/out/occt`; generated bridge output is reproducible with `native/scripts/generate-wrapper.ps1`.
