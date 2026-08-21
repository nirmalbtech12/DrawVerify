// SWIG interface file for the OCCT C# wrapper.
// Consumed by native/scripts/generate-wrapper.ps1. Generated output lands in
// src/DrawVerify.OcctWrapper/Generated — do not hand-edit.
// Pin the OCCT version alongside this file in docs/deployment.md.

%module(directors="1") occcsharp

%{
#include <StepReader includes...>  // added at generation time
%}

// Only the OCCT surface DrawVerify actually needs: STEP reading,
// BRep shape queries, volume, bounding box, face/normal traversal.
%include "csharp/std_string.i"
%include "csharp/std_vector.i"

// Shape and reader handles (backed by native handles in DrawVerify.OcctService.SafeHandles)
typedef void* TopoDS_Shape;    // opaque native handle
typedef void* StepReader;      // opaque native handle

// Geometry query surface (mirrors DrawVerify.Geometry.IGeometryService contract)
TopoDS_Shape Step_Read(const char* filePath);
double       Shape_Volume(TopoDS_Shape handle);
void         Shape_BoundingBox(TopoDS_Shape handle, double& xMin, double& yMin, double& zMin,
                               double& xMax, double& yMax, double& zMax);
void         Shape_Release(TopoDS_Shape handle);