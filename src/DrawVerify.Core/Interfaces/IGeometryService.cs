namespace DrawVerify.Core.Interfaces;

/// <summary>
/// THE geometry abstraction. Core/Application/App depend only on this contract.
/// The only implementation is DrawVerify.OcctService (OCCT behind the scenes).
/// No OCCT types ever leak through this interface.
/// </summary>
public interface IGeometryService
{
}
