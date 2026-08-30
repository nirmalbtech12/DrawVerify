#include "DrawVerify/Occt/Facade.hxx"
#include <Standard_Version.hxx>
#include <STEPControl_Reader.hxx>
#include <HLRBRep_Algo.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <Standard_Failure.hxx>
namespace DrawVerify::Occt { std::string OcctRuntime::GetOcctVersion(){return OCC_VERSION_STRING;} std::string OcctRuntime::GetWrapperVersion(){return "1.0.0";} bool OcctRuntime::IsStepSupported(){return true;} bool OcctRuntime::IsProjectionSupported(){return true;} bool OcctRuntime::IsMeshingSupported(){return true;} OperationResult OcctRuntime::CheckReadiness(){ try { STEPControl_Reader stepReader; opencascade::handle<HLRBRep_Algo> projection=new HLRBRep_Algo; BRepMesh_IncrementalMesh mesher; (void)stepReader; (void)projection; (void)mesher; return {ErrorCode::Success,"OCCT 8.0.0 STEP, HLR, and meshing services loaded",{}}; } catch(const Standard_Failure& e) { return {ErrorCode::NativeFailure,e.what(),{{ErrorCode::NativeFailure,"Readiness",e.what()}}}; } catch(const std::exception& e) { return {ErrorCode::NativeFailure,e.what(),{{ErrorCode::NativeFailure,"Readiness",e.what()}}}; } catch(...) { return {ErrorCode::UnknownFailure,"Unknown native readiness failure",{{ErrorCode::UnknownFailure,"Readiness","Unknown native readiness failure"}}}; } } }
