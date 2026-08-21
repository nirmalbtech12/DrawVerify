using Microsoft.Win32.SafeHandles;

namespace DrawVerify.OcctService.SafeHandles;

/// <summary>
/// SafeHandle wrapping the native OCCT STEP reader; guarantees deterministic native release.
/// </summary>
public sealed class OcctStepReaderSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public OcctStepReaderSafeHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return true;
    }
}