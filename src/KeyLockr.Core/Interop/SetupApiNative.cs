using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyLockr.Core.Interop;

internal static class SetupApiNative
{
    public static readonly Guid GuidDevClassKeyboard = new("4D36E96B-E325-11CE-BFC1-08002BE10318");

    [Flags]
    public enum Digcf : uint
    {
        Default = 0x00000001,
        Present = 0x00000002,
        AllClasses = 0x00000004,
        Profile = 0x00000008,
        DeviceInterface = 0x00000010
    }

    public enum Spdrp : uint
    {
        DevDesc = 0x00000000,
        HardwareId = 0x00000001,
        FriendlyName = 0x0000000C,
        LocationInformation = 0x0000000D,
        Class = 0x00000007,
        EnumeratorName = 0x00000016,
        ConfigFlags = 0x00000024
    }

    public enum Dif : uint
    {
        PropertyChange = 0x00000012
    }

    public enum Dics : uint
    {
        Disable = 0x00000000,
        Enable = 0x00000001,
        PropChange = 0x00000003
    }

    [Flags]
    public enum DicsFlag : uint
    {
        Global = 0x00000001,
        ConfigSpecific = 0x00000002,
        GlobalConfigSpecific = 0x00000003
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SpDevinfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SpClassinstallHeader
    {
        public uint cbSize;
        public Dif InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SpPropchangeParams
    {
        public SpClassinstallHeader ClassInstallHeader;
        public Dics StateChange;
        public DicsFlag Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, Digcf Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(SafeDeviceInfoSetHandle DeviceInfoSet, uint MemberIndex, ref SpDevinfoData DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceId(SafeDeviceInfoSetHandle DeviceInfoSet, ref SpDevinfoData DeviceInfoData, StringBuilder DeviceInstanceId, int DeviceInstanceIdSize, out int RequiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceRegistryProperty(SafeDeviceInfoSetHandle DeviceInfoSet, ref SpDevinfoData DeviceInfoData, Spdrp Property, out uint PropertyRegDataType, byte[]? PropertyBuffer, int PropertyBufferSize, out int RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiSetClassInstallParams(SafeDeviceInfoSetHandle DeviceInfoSet, ref SpDevinfoData DeviceInfoData, ref SpPropchangeParams ClassInstallParams, int ClassInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiCallClassInstaller(Dif InstallFunction, SafeDeviceInfoSetHandle DeviceInfoSet, ref SpDevinfoData DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    public static void ThrowIfError(bool success)
    {
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
