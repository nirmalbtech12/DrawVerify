%module DrawVerifyOcct
%include "exception.i"
%{
#include "DrawVerify/Occt/Facade.hxx"
#include <Standard_Failure.hxx>
%}
// Value-returning APIs cannot express an OperationResult.  Convert every
// native failure here so no OCCT/C++ exception can unwind through SWIG's ABI.
%exception {
  try { $action }
  catch (const Standard_Failure& error) { SWIG_exception(SWIG_RuntimeError, error.what()); }
  catch (const std::exception& error) { SWIG_exception(SWIG_RuntimeError, error.what()); }
  catch (...) { SWIG_exception(SWIG_RuntimeError, "Unknown native DrawVerify OCCT failure."); }
}
%include "std_string.i"
%include "std_vector.i"
namespace std {
%template(IntVector) vector<int>;
%template(Point2Vector) vector<DrawVerify::Occt::Point2>;
%template(Point3Vector) vector<DrawVerify::Occt::Point3>;
%template(DiagnosticVector) vector<DrawVerify::Occt::Diagnostic>;
%template(VertexInfoVector) vector<DrawVerify::Occt::VertexInfo>;
%template(EdgeInfoVector) vector<DrawVerify::Occt::EdgeInfo>;
%template(FaceInfoVector) vector<DrawVerify::Occt::FaceInfo>;
%template(Line2Vector) vector<DrawVerify::Occt::Line2>;
%template(Arc2Vector) vector<DrawVerify::Occt::Arc2>;
%template(Circle2Vector) vector<DrawVerify::Occt::Circle2>;
%template(Polyline2Vector) vector<DrawVerify::Occt::Polyline2>;
%template(MeshVertexVector) vector<DrawVerify::Occt::MeshVertex>;
%template(MeshTriangleVector) vector<DrawVerify::Occt::MeshTriangle>;
}
%include "DrawVerify/Occt/Facade.hxx"
