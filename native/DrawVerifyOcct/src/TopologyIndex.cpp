#include "Internal.hxx"

#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <NCollection_IndexedMap.hxx>
#include <TopTools_ShapeMapHasher.hxx>

#include <algorithm>
#include <stdexcept>

namespace DrawVerify::Occt
{
namespace
{
using ShapeMap = NCollection_IndexedMap<TopoDS_Shape, TopTools_ShapeMapHasher>;

ShapeMap mapShapes(const TopoDS_Shape& shape, TopAbs_ShapeEnum type)
{
  ShapeMap result;
  TopExp::MapShapes(shape, type, result);
  return result;
}
}

TopoDS_Shape ShapeById(const TopoDS_Shape& shape, TopAbs_ShapeEnum type, int id)
{
  const auto shapes = mapShapes(shape, type);
  if (id <= 0 || id > shapes.Extent())
  {
    throw std::out_of_range("Unknown topology ID");
  }
  return shapes(id);
}

int ShapeCount(const TopoDS_Shape& shape, TopAbs_ShapeEnum type)
{
  return mapShapes(shape, type).Extent();
}

int ShapeId(const TopoDS_Shape& shape, TopAbs_ShapeEnum type, const TopoDS_Shape& child)
{
  return mapShapes(shape, type).FindIndex(child);
}

std::vector<int> ChildIds(const TopoDS_Shape& parent, TopAbs_ShapeEnum childType,
                          const TopoDS_Shape& model, TopAbs_ShapeEnum modelType)
{
  const auto allShapes = mapShapes(model, modelType);
  std::vector<int> result;
  for (TopExp_Explorer explorer(parent, childType); explorer.More(); explorer.Next())
  {
    const int id = allShapes.FindIndex(explorer.Current());
    if (id > 0 && std::find(result.begin(), result.end(), id) == result.end())
    {
      result.push_back(id);
    }
  }
  return result;
}
}
