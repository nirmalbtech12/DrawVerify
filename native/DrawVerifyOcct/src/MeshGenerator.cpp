#include "Internal.hxx"

#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Tool.hxx>
#include <Poly_Triangulation.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>

#include <cmath>

namespace DrawVerify::Occt
{
namespace
{
void addFaceNormal(MeshVertex& vertex, const Vector3& normal)
{
  vertex.normal.x += normal.x;
  vertex.normal.y += normal.y;
  vertex.normal.z += normal.z;
}

Vector3 triangleNormal(const Point3& first, const Point3& second, const Point3& third, bool reversed)
{
  const double ax = second.x - first.x;
  const double ay = second.y - first.y;
  const double az = second.z - first.z;
  const double bx = third.x - first.x;
  const double by = third.y - first.y;
  const double bz = third.z - first.z;
  const double sign = reversed ? -1.0 : 1.0;
  return {sign * (ay * bz - az * by), sign * (az * bx - ax * bz), sign * (ax * by - ay * bx)};
}

void normalize(Vector3& normal)
{
  const double magnitude = std::sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z);
  if (magnitude > 0.0)
  {
    normal.x /= magnitude;
    normal.y /= magnitude;
    normal.z /= magnitude;
  }
}
}

MeshData Mesh(const std::shared_ptr<Model::Impl>& impl, const MeshOptions& options)
{
  if (!impl || impl->shape.IsNull())
  {
    throw std::runtime_error("Meshing requires a loaded model");
  }
  if (options.linearDeflection <= 0 || options.angularDeflection <= 0)
  {
    throw std::invalid_argument("Mesh deflections must be positive");
  }

  BRepMesh_IncrementalMesh mesher(impl->shape, options.linearDeflection, options.relative,
                                  options.angularDeflection, true);
  MeshData result;
  for (TopExp_Explorer explorer(impl->shape, TopAbs_FACE); explorer.More(); explorer.Next())
  {
    const auto face = TopoDS::Face(explorer.Current());
    const int faceId = ShapeId(impl->shape, TopAbs_FACE, face);
    TopLoc_Location location;
    const auto triangulation = BRep_Tool::Triangulation(face, location);
    if (triangulation.IsNull())
    {
      continue;
    }

    const int vertexOffset = static_cast<int>(result.vertices.size());
    for (int node = 1; node <= triangulation->NbNodes(); ++node)
    {
      const auto point = triangulation->Node(node).Transformed(location.Transformation());
      result.vertices.push_back({{point.X(), point.Y(), point.Z()}, {}, faceId});
    }
    for (int triangle = 1; triangle <= triangulation->NbTriangles(); ++triangle)
    {
      int first = 0;
      int second = 0;
      int third = 0;
      triangulation->Triangle(triangle).Get(first, second, third);
      const int firstIndex = vertexOffset + first - 1;
      const int secondIndex = vertexOffset + second - 1;
      const int thirdIndex = vertexOffset + third - 1;
      const auto normal = triangleNormal(result.vertices[firstIndex].point, result.vertices[secondIndex].point,
                                         result.vertices[thirdIndex].point, face.Orientation() == TopAbs_REVERSED);
      addFaceNormal(result.vertices[firstIndex], normal);
      addFaceNormal(result.vertices[secondIndex], normal);
      addFaceNormal(result.vertices[thirdIndex], normal);
      result.triangles.push_back({firstIndex, secondIndex, thirdIndex, faceId});
    }
    for (int index = vertexOffset; index < static_cast<int>(result.vertices.size()); ++index)
    {
      normalize(result.vertices[index].normal);
    }
  }
  return result;
}
}
