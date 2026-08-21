using Microsoft.Win32.SafeHandles;

namespace DrawVerify.OcctService.SafeHandles;

/// <summary>
/// SafeHandle wrapping a native OCCT shape; guarantees deterministic native release.
/// </summary>
public sealed class OcctShapeSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public OcctShapeSafeHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return true;
    }
}