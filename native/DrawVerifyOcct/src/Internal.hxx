#pragma once
#include "DrawVerify/Occt/Facade.hxx"
#include <TopoDS_Shape.hxx>
#include <TopAbs_ShapeEnum.hxx>
namespace DrawVerify::Occt {
struct Model::Impl { TopoDS_Shape shape; std::string unit="unknown"; double millimeters=0; };
TopoDS_Shape ShapeById(const TopoDS_Shape&, TopAbs_ShapeEnum, int); int ShapeCount(const TopoDS_Shape&, TopAbs_ShapeEnum); int ShapeId(const TopoDS_Shape&, TopAbs_ShapeEnum, const TopoDS_Shape&); std::vector<int> ChildIds(const TopoDS_Shape&, TopAbs_ShapeEnum, const TopoDS_Shape&, TopAbs_ShapeEnum);
BoundingBox3 Box(const std::shared_ptr<Model::Impl>&); ModelSummary Summary(const std::shared_ptr<Model::Impl>&); std::vector<VertexInfo> Vertices(const std::shared_ptr<Model::Impl>&); std::vector<EdgeInfo> Edges(const std::shared_ptr<Model::Impl>&); EdgeInfo Edge(const std::shared_ptr<Model::Impl>&,int); std::vector<FaceInfo> Faces(const std::shared_ptr<Model::Impl>&); FaceInfo Face(const std::shared_ptr<Model::Impl>&,int); std::vector<int> FaceEdges(const std::shared_ptr<Model::Impl>&,int); std::vector<int> EdgeVertices(const std::shared_ptr<Model::Impl>&,int); std::vector<int> SolidFaces(const std::shared_ptr<Model::Impl>&,int); BoundingBox3 FaceBox(const std::shared_ptr<Model::Impl>&,int); BoundingBox3 EdgeBox(const std::shared_ptr<Model::Impl>&,int); OperationResult Validate(const std::shared_ptr<Model::Impl>&); double EdgeLength(const std::shared_ptr<Model::Impl>&,int); double FaceArea(const std::shared_ptr<Model::Impl>&,int); double SurfaceArea(const std::shared_ptr<Model::Impl>&); double Volume(const std::shared_ptr<Model::Impl>&); ProjectionResult Project(const std::shared_ptr<Model::Impl>&,ProjectionView,const Vector3*,const Vector3*); MeshData Mesh(const std::shared_ptr<Model::Impl>&,const MeshOptions&);
}
