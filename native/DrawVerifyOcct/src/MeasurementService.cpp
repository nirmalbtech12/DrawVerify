#include "Internal.hxx"

#include <BRepAdaptor_Curve.hxx>
#include <BRepGProp.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GProp_GProps.hxx>
#include <TopoDS.hxx>

namespace DrawVerify::Occt
{
double EdgeLength(const std::shared_ptr<Model::Impl>& impl, int id)
{
  BRepAdaptor_Curve curve(TopoDS::Edge(ShapeById(impl->shape, TopAbs_EDGE, id)));
  return GCPnts_AbscissaPoint::Length(curve, curve.FirstParameter(), curve.LastParameter());
}

double FaceArea(const std::shared_ptr<Model::Impl>& impl, int id)
{
  GProp_GProps properties;
  BRepGProp::SurfaceProperties(ShapeById(impl->shape, TopAbs_FACE, id), properties);
  return properties.Mass();
}

double SurfaceArea(const std::shared_ptr<Model::Impl>& impl)
{
  if (!impl || impl->shape.IsNull())
  {
    return 0;
  }
  GProp_GProps properties;
  BRepGProp::SurfaceProperties(impl->shape, properties);
  return properties.Mass();
}

double Volume(const std::shared_ptr<Model::Impl>& impl)
{
  if (!impl || impl->shape.IsNull())
  {
    return 0;
  }
  GProp_GProps properties;
  BRepGProp::VolumeProperties(impl->shape, properties);
  return properties.Mass();
}
}
