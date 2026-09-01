#include "Internal.hxx"

#include <BRepCheck_Analyzer.hxx>
#include <Standard_Failure.hxx>

namespace DrawVerify::Occt
{
OperationResult Validate(const std::shared_ptr<Model::Impl>& impl)
{
  if (!impl || impl->shape.IsNull())
  {
    return {ErrorCode::EmptyModel, "Model is empty", {{ErrorCode::EmptyModel, "ValidateModel", "Model is empty"}}};
  }

  try
  {
    BRepCheck_Analyzer analyzer(impl->shape);
    if (analyzer.IsValid())
    {
      return {ErrorCode::Success, "Model is valid", {}};
    }
    return {ErrorCode::InvalidModel, "OCCT shape validation failed",
            {{ErrorCode::InvalidModel, "ValidateModel", "BRepCheck_Analyzer reported invalid shape"}}};
  }
  catch (const Standard_Failure& error)
  {
    return {ErrorCode::OcctFailure, error.what(), {{ErrorCode::OcctFailure, "ValidateModel", error.what()}}};
  }
  catch (const std::exception& error)
  {
    return {ErrorCode::NativeFailure, error.what(), {{ErrorCode::NativeFailure, "ValidateModel", error.what()}}};
  }
}
}
