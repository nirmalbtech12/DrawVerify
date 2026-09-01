#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <IFSelect_ReturnStatus.hxx>
#include <STEPControl_Writer.hxx>
#include <TopoDS_Shape.hxx>
#include <gp_Pnt.hxx>

#include <filesystem>
#include <iostream>
#include <string>

namespace
{
bool writeStep(const TopoDS_Shape& shape, const std::filesystem::path& path)
{
  STEPControl_Writer writer;
  if (writer.Transfer(shape, STEPControl_AsIs) != IFSelect_RetDone)
  {
    return false;
  }
  return writer.Write(path.string().c_str()) == IFSelect_RetDone;
}
}

int main(int argc, char** argv)
{
  if (argc != 2)
  {
    std::cerr << "Expected output fixture directory.\n";
    return 2;
  }

  const std::filesystem::path outputDirectory(argv[1]);
  std::filesystem::create_directories(outputDirectory);

  const TopoDS_Shape cube = BRepPrimAPI_MakeBox(10.0, 10.0, 10.0).Shape();
  const TopoDS_Shape cylinder = BRepPrimAPI_MakeCylinder(5.0, 10.0).Shape();
  const TopoDS_Shape plate = BRepAlgoAPI_Cut(
    BRepPrimAPI_MakeBox(20.0, 20.0, 5.0).Shape(),
    BRepPrimAPI_MakeCylinder(3.0, 5.0).Shape()).Shape();
  const TopoDS_Shape steppedBlock = BRepAlgoAPI_Fuse(
    BRepPrimAPI_MakeBox(20.0, 10.0, 5.0).Shape(),
    BRepPrimAPI_MakeBox(gp_Pnt(0.0, 0.0, 5.0), 10.0, 10.0, 5.0).Shape()).Shape();

  const bool written = writeStep(cube, outputDirectory / "cube.step")
    && writeStep(cylinder, outputDirectory / "cylinder.step")
    && writeStep(plate, outputDirectory / "plate-through-hole.step")
    && writeStep(steppedBlock, outputDirectory / "stepped-block.step");
  return written ? 0 : 1;
}
