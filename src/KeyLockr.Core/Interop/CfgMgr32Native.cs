using System.Runtime.InteropServices;

namespace KeyLockr.Core.Interop;

internal static class CfgMgr32Native
{
    public const int CrSuccess = 0;

    [Flags]
    public enum CmProblem : uint
    {
        None = 0,
        Disabled = 0x00000022
    }

    [Flags]
    public enum DevNodeStatus : uint
    {
        None = 0,
        DnRootEnumerated = 0x00000001,
        DnDriverLoaded = 0x00000002,
        DnEnumLoaded = 0x00000004,
        DnStarted = 0x00000008,
        DnDisabled = 0x00000020,
        DnRemoved = 0x00000080,
        DnWillBeRemoved = 0x00000100,
        DnNeedRestart = 0x00000400,
        DnNotFirstTime = 0x00000800
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_DevNode_Status(out DevNodeStatus pulStatus, out CmProblem pulProblemNumber, uint dnDevInst, int ulFlags);
}
