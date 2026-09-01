#include "Internal.hxx"

#include <BRepBndLib.hxx>
#include <Bnd_Box.hxx>

namespace DrawVerify::Occt
{
namespace
{
BoundingBox3 box(const TopoDS_Shape& shape)
{
  BoundingBox3 result;
  if (shape.IsNull())
  {
    return result;
  }
  Bnd_Box nativeBox;
  BRepBndLib::Add(shape, nativeBox);
  if (!nativeBox.IsVoid())
  {
    nativeBox.Get(result.minimum.x, result.minimum.y, result.minimum.z,
                  result.maximum.x, result.maximum.y, result.maximum.z);
    result.isVoid = false;
  }
  return result;
}
}

BoundingBox3 Box(const std::shared_ptr<Model::Impl>& impl)
{
  return impl ? box(impl->shape) : BoundingBox3{};
}

ModelSummary Summary(const std::shared_ptr<Model::Impl>& impl)
{
  ModelSummary result;
  if (!impl || impl->shape.IsNull())
  {
    return result;
  }

  const auto& shape = impl->shape;
  result.vertexCount = ShapeCount(shape, TopAbs_VERTEX);
  result.edgeCount = ShapeCount(shape, TopAbs_EDGE);
  result.wireCount = ShapeCount(shape, TopAbs_WIRE);
  result.faceCount = ShapeCount(shape, TopAbs_FACE);
  result.shellCount = ShapeCount(shape, TopAbs_SHELL);
  result.solidCount = ShapeCount(shape, TopAbs_SOLID);
  result.compoundCount = ShapeCount(shape, TopAbs_COMPOUND);
  result.boundingBox = box(shape);
  result.surfaceArea = SurfaceArea(impl);
  result.volume = Volume(impl);
  result.unit = impl->unit;
  result.unitScaleToMillimeters = impl->millimeters;
  result.validity = Validate(impl).Succeeded() ? ValidationStatus::Valid : ValidationStatus::Invalid;
  return result;
}

std::vector<int> FaceEdges(const std::shared_ptr<Model::Impl>& impl, int id)
{
  return ChildIds(ShapeById(impl->shape, TopAbs_FACE, id), TopAbs_EDGE, impl->shape, TopAbs_EDGE);
}

std::vector<int> EdgeVertices(const std::shared_ptr<Model::Impl>& impl, int id)
{
  return ChildIds(ShapeById(impl->shape, TopAbs_EDGE, id), TopAbs_VERTEX, impl->shape, TopAbs_VERTEX);
}

std::vector<int> SolidFaces(const std::shared_ptr<Model::Impl>& impl, int id)
{
  return ChildIds(ShapeById(impl->shape, TopAbs_SOLID, id), TopAbs_FACE, impl->shape, TopAbs_FACE);
}

BoundingBox3 FaceBox(const std::shared_ptr<Model::Impl>& impl, int id)
{
  return box(ShapeById(impl->shape, TopAbs_FACE, id));
}

BoundingBox3 EdgeBox(const std::shared_ptr<Model::Impl>& impl, int id)
{
  return box(ShapeById(impl->shape, TopAbs_EDGE, id));
}
}
