using Microsoft.Win32.SafeHandles;

namespace KeyLockr.Core.Interop;

internal sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeDeviceInfoSetHandle() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return SetupApiNative.SetupDiDestroyDeviceInfoList(handle);
    }
}
