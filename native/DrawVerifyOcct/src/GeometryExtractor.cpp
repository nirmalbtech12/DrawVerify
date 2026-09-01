#include "Internal.hxx"

#include <BRepAdaptor_Curve.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <BRepBndLib.hxx>
#include <BRep_Tool.hxx>
#include <Bnd_Box.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GeomAbs_CurveType.hxx>
#include <GeomAbs_SurfaceType.hxx>
#include <Geom_BSplineCurve.hxx>
#include <TopoDS.hxx>

namespace DrawVerify::Occt
{
namespace
{
Point3 point(const gp_Pnt& value) { return {value.X(), value.Y(), value.Z()}; }
Vector3 vector(const gp_Dir& value) { return {value.X(), value.Y(), value.Z()}; }

BoundingBox3 box(const TopoDS_Shape& shape)
{
  Bnd_Box nativeBox;
  BRepBndLib::Add(shape, nativeBox);
  BoundingBox3 result;
  if (!nativeBox.IsVoid())
  {
    nativeBox.Get(result.minimum.x, result.minimum.y, result.minimum.z,
                  result.maximum.x, result.maximum.y, result.maximum.z);
    result.isVoid = false;
  }
  return result;
}

GeometryType curveType(GeomAbs_CurveType type)
{
  switch (type)
  {
    case GeomAbs_Line: return GeometryType::Line;
    case GeomAbs_Circle: return GeometryType::Circle;
    case GeomAbs_Ellipse: return GeometryType::Ellipse;
    case GeomAbs_BSplineCurve: return GeometryType::BSpline;
    case GeomAbs_BezierCurve: return GeometryType::Bezier;
    default: return GeometryType::Other;
  }
}

SurfaceType surfaceType(GeomAbs_SurfaceType type)
{
  switch (type)
  {
    case GeomAbs_Plane: return SurfaceType::Plane;
    case GeomAbs_Cylinder: return SurfaceType::Cylinder;
    case GeomAbs_Cone: return SurfaceType::Cone;
    case GeomAbs_Sphere: return SurfaceType::Sphere;
    case GeomAbs_Torus: return SurfaceType::Torus;
    case GeomAbs_BSplineSurface: return SurfaceType::BSpline;
    case GeomAbs_BezierSurface: return SurfaceType::Bezier;
    default: return SurfaceType::Other;
  }
}
}

std::vector<VertexInfo> Vertices(const std::shared_ptr<Model::Impl>& impl)
{
  std::vector<VertexInfo> result;
  if (!impl)
  {
    return result;
  }
  const int count = ShapeCount(impl->shape, TopAbs_VERTEX);
  result.reserve(count);
  for (int id = 1; id <= count; ++id)
  {
    result.push_back({id, point(BRep_Tool::Pnt(TopoDS::Vertex(ShapeById(impl->shape, TopAbs_VERTEX, id))))});
  }
  return result;
}

EdgeInfo Edge(const std::shared_ptr<Model::Impl>& impl, int id)
{
  const auto edge = TopoDS::Edge(ShapeById(impl->shape, TopAbs_EDGE, id));
  BRepAdaptor_Curve curve(edge);
  EdgeInfo result;
  result.id = id;
  result.geometryType = curveType(curve.GetType());
  result.startPoint = point(curve.Value(curve.FirstParameter()));
  result.endPoint = point(curve.Value(curve.LastParameter()));
  result.length = GCPnts_AbscissaPoint::Length(curve, curve.FirstParameter(), curve.LastParameter());
  result.closed = edge.Closed();
  result.degenerate = BRep_Tool::Degenerated(edge);
  result.boundingBox = box(edge);
  result.startParameter = curve.FirstParameter();
  result.endParameter = curve.LastParameter();
  if (result.geometryType == GeometryType::Line)
  {
    result.direction = vector(curve.Line().Direction());
  }
  else if (result.geometryType == GeometryType::Circle)
  {
    const auto circle = curve.Circle();
    result.center = point(circle.Location());
    result.radius = circle.Radius();
    result.axis = vector(circle.Axis().Direction());
  }
  else if (result.geometryType == GeometryType::Ellipse)
  {
    const auto ellipse = curve.Ellipse();
    result.center = point(ellipse.Location());
    result.majorRadius = ellipse.MajorRadius();
    result.minorRadius = ellipse.MinorRadius();
    result.axis = vector(ellipse.Axis().Direction());
  }
  else if (result.geometryType == GeometryType::BSpline)
  {
    result.degree = curve.BSpline()->Degree();
    result.rational = curve.BSpline()->IsRational();
  }
  return result;
}

std::vector<EdgeInfo> Edges(const std::shared_ptr<Model::Impl>& impl)
{
  std::vector<EdgeInfo> result;
  if (!impl)
  {
    return result;
  }
  const int count = ShapeCount(impl->shape, TopAbs_EDGE);
  result.reserve(count);
  for (int id = 1; id <= count; ++id)
  {
    result.push_back(Edge(impl, id));
  }
  return result;
}

FaceInfo Face(const std::shared_ptr<Model::Impl>& impl, int id)
{
  const auto face = TopoDS::Face(ShapeById(impl->shape, TopAbs_FACE, id));
  BRepAdaptor_Surface surface(face);
  FaceInfo result;
  result.id = id;
  result.surfaceType = surfaceType(surface.GetType());
  result.area = FaceArea(impl, id);
  result.orientation = static_cast<int>(face.Orientation());
  result.boundingBox = box(face);
  if (result.surfaceType == SurfaceType::Plane)
  {
    const auto plane = surface.Plane();
    result.origin = point(plane.Location());
    result.normal = vector(plane.Axis().Direction());
    result.xDirection = vector(plane.Position().XDirection());
    result.yDirection = vector(plane.Position().YDirection());
  }
  else if (result.surfaceType == SurfaceType::Cylinder)
  {
    const auto cylinder = surface.Cylinder();
    result.origin = point(cylinder.Location());
    result.axis = vector(cylinder.Axis().Direction());
    result.radius = cylinder.Radius();
  }
  else if (result.surfaceType == SurfaceType::Cone)
  {
    const auto cone = surface.Cone();
    result.origin = point(cone.Location());
    result.axis = vector(cone.Axis().Direction());
    result.radius = cone.RefRadius();
    result.semiAngle = cone.SemiAngle();
  }
  else if (result.surfaceType == SurfaceType::Sphere)
  {
    const auto sphere = surface.Sphere();
    result.origin = point(sphere.Location());
    result.radius = sphere.Radius();
  }
  else if (result.surfaceType == SurfaceType::Torus)
  {
    const auto torus = surface.Torus();
    result.origin = point(torus.Location());
    result.axis = vector(torus.Axis().Direction());
    result.majorRadius = torus.MajorRadius();
    result.minorRadius = torus.MinorRadius();
  }
  return result;
}

std::vector<FaceInfo> Faces(const std::shared_ptr<Model::Impl>& impl)
{
  std::vector<FaceInfo> result;
  if (!impl)
  {
    return result;
  }
  const int count = ShapeCount(impl->shape, TopAbs_FACE);
  result.reserve(count);
  for (int id = 1; id <= count; ++id)
  {
    result.push_back(Face(impl, id));
  }
  return result;
}
}
