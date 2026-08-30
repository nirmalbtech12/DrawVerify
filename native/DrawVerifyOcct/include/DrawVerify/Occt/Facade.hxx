#pragma once
#include <memory>
#include <string>
#include <vector>
namespace DrawVerify::Occt {
enum class ErrorCode { Success, InvalidArgument, FileNotFound, UnsupportedFile, StepReadFailed, StepTransferFailed, EmptyModel, InvalidModel, ProjectionFailed, MeshingFailed, OcctFailure, NativeFailure, UnknownFailure };
enum class ValidationStatus { Valid, Invalid, ReadFailure, TransferFailure, EmptyShape, Unsupported, InternalOcctError };
enum class GeometryType { Unknown, Point, Line, Circle, Ellipse, BSpline, Bezier, Other };
enum class SurfaceType { Unknown, Plane, Cylinder, Cone, Sphere, Torus, BSpline, Bezier, Other };
enum class ShapeType { Unknown, Compound, CompSolid, Solid, Shell, Face, Wire, Edge, Vertex };
enum class ProjectionView { Front, Rear, Top, Bottom, Left, Right, Custom };
struct Point3 { double x=0,y=0,z=0; }; struct Vector3 { double x=0,y=0,z=0; }; struct Point2 { double x=0,y=0; };
struct BoundingBox3 { Point3 minimum; Point3 maximum; bool isVoid=true; };
struct Diagnostic { ErrorCode code=ErrorCode::Success; std::string operation; std::string message; };
struct OperationResult { ErrorCode code=ErrorCode::Success; std::string message; std::vector<Diagnostic> diagnostics; bool Succeeded() const { return code==ErrorCode::Success; } };
struct ModelSummary { int vertexCount=0,edgeCount=0,wireCount=0,faceCount=0,shellCount=0,solidCount=0,compoundCount=0; BoundingBox3 boundingBox; double surfaceArea=0,volume=0; Point3 centerOfMass; ValidationStatus validity=ValidationStatus::EmptyShape; std::string unit; double unitScaleToMillimeters=0; };
struct VertexInfo { int id=0; Point3 point; };
struct EdgeInfo { int id=0; GeometryType geometryType=GeometryType::Unknown; Point3 startPoint,endPoint; BoundingBox3 boundingBox; double length=0; bool closed=false,degenerate=false; Point3 center; Vector3 direction,axis; double radius=0,startParameter=0,endParameter=0,majorRadius=0,minorRadius=0; int degree=0; bool rational=false; };
struct FaceInfo { int id=0; SurfaceType surfaceType=SurfaceType::Unknown; double area=0; int orientation=0; BoundingBox3 boundingBox; Point3 origin; Vector3 normal,xDirection,yDirection,axis; double radius=0,majorRadius=0,minorRadius=0,semiAngle=0; };
struct Line2 { Point2 start,end; bool hidden=false; }; struct Circle2 { Point2 center; double radius=0; bool hidden=false; }; struct Arc2 { Point2 center; double radius=0,startParameter=0,endParameter=0; bool hidden=false; }; struct Polyline2 { std::vector<Point2> points; bool hidden=false; };
struct MeshVertex { Point3 point; Vector3 normal; int faceId=0; }; struct MeshTriangle { int first=0,second=0,third=0,faceId=0; }; struct MeshData { std::vector<MeshVertex> vertices; std::vector<MeshTriangle> triangles; }; struct MeshOptions { double linearDeflection=0.1,angularDeflection=0.5; bool relative=false; };
struct ProjectionResult { std::vector<Line2> visibleLines,hiddenLines; std::vector<Arc2> visibleArcs,hiddenArcs; std::vector<Circle2> visibleCircles,hiddenCircles; std::vector<Polyline2> visiblePolylines,hiddenPolylines; double approximationTolerance=1e-3; };
class Model { public: struct Impl; Model(); ~Model(); Model(const Model&); Model& operator=(const Model&); Model(Model&&) noexcept; Model& operator=(Model&&) noexcept; bool IsNull() const; ModelSummary GetModelSummary() const; BoundingBox3 GetBoundingBox() const; std::vector<VertexInfo> GetVertices() const; std::vector<EdgeInfo> GetEdges() const; EdgeInfo GetEdge(int id) const; std::vector<FaceInfo> GetFaces() const; FaceInfo GetFace(int id) const; std::vector<int> GetFaceEdges(int faceId) const; std::vector<int> GetEdgeVertices(int edgeId) const; std::vector<int> GetSolidFaces(int solidId) const; BoundingBox3 GetFaceBoundingBox(int faceId) const; BoundingBox3 GetEdgeBoundingBox(int edgeId) const; double GetEdgeLength(int edgeId) const; double GetFaceArea(int faceId) const; double GetModelSurfaceArea() const; double GetModelVolume() const; double Distance(const Point3&,const Point3&) const; OperationResult ValidateModel() const; ProjectionResult GenerateProjection(ProjectionView) const; ProjectionResult GenerateProjectionCustom(const Vector3&,const Vector3&) const; MeshData GenerateMesh(const MeshOptions&) const; std::string GetModelUnit() const; double GetModelUnitScaleToMillimeters() const; private: std::shared_ptr<Impl> impl_; explicit Model(std::shared_ptr<Impl>); friend class StepReader; };
class StepReader { public: StepReader(); OperationResult LoadStep(const std::string& path); Model GetModel() const; int GetRootCount() const; int GetTransferredRootCount() const; OperationResult GetTransferStatus() const; std::vector<Diagnostic> GetTransferDiagnostics() const; private: Model model_; int roots_=0,transferred_=0; OperationResult result_; };
class OcctRuntime { public: static std::string GetOcctVersion(); static std::string GetWrapperVersion(); static bool IsStepSupported(); static bool IsProjectionSupported(); static bool IsMeshingSupported(); static OperationResult CheckReadiness(); };
}
