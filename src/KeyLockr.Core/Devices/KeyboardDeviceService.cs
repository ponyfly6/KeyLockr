using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using KeyLockr.Core.Interop;

namespace KeyLockr.Core.Devices;

public interface IKeyboardDeviceService
{
    Task<IReadOnlyList<KeyboardDevice>> GetKeyboardsAsync(CancellationToken cancellationToken = default);
    Task DisableAsync(string instanceId, CancellationToken cancellationToken = default);
    Task EnableAsync(string instanceId, CancellationToken cancellationToken = default);
}

public sealed class KeyboardDeviceService : IKeyboardDeviceService
{
    public Task<IReadOnlyList<KeyboardDevice>> GetKeyboardsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var classGuid = SetupApiNative.GuidDevClassKeyboard;
        using var deviceInfoSet = SetupApiNative.SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, SetupApiNative.Digcf.Present);
        if (deviceInfoSet.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var result = new List<KeyboardDevice>();
        var deviceInfoData = new SetupApiNative.SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SetupApiNative.SpDevinfoData>() };
        uint index = 0;
        const int ErrorNoMoreItems = 259;

        while (SetupApiNative.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var instanceId = GetInstanceId(deviceInfoSet, ref deviceInfoData);
            var description = GetDeviceDescription(deviceInfoSet, ref deviceInfoData) ?? instanceId;
            var hardwareIds = GetHardwareIds(deviceInfoSet, ref deviceInfoData);
            var location = GetStringProperty(deviceInfoSet, ref deviceInfoData, SetupApiNative.Spdrp.LocationInformation);
            var status = GetStatus(deviceInfoData.DevInst);

            var isRemovable = location != null && location.Contains("USB", StringComparison.OrdinalIgnoreCase);

            result.Add(new KeyboardDevice(instanceId, description, hardwareIds, location, status.IsEnabled, status.IsPresent, isRemovable));
            index++;
        }

        var lastError = Marshal.GetLastWin32Error();
        if (lastError != ErrorNoMoreItems)
        {
            throw new Win32Exception(lastError);
        }

        return Task.FromResult<IReadOnlyList<KeyboardDevice>>(result);
    }

    public async Task DisableAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await ChangeDeviceStateAsync(instanceId, SetupApiNative.Dics.Disable, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnableAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await ChangeDeviceStateAsync(instanceId, SetupApiNative.Dics.Enable, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ChangeDeviceStateAsync(string instanceId, SetupApiNative.Dics state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var classGuid = SetupApiNative.GuidDevClassKeyboard;
        using var deviceInfoSet = SetupApiNative.SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, SetupApiNative.Digcf.Present);
        if (deviceInfoSet.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var deviceInfoData = new SetupApiNative.SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SetupApiNative.SpDevinfoData>() };
        uint index = 0;
        const int ErrorNoMoreItems = 259;

        while (SetupApiNative.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateId = GetInstanceId(deviceInfoSet, ref deviceInfoData);
            if (string.Equals(candidateId, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                ApplyPropChange(deviceInfoSet, ref deviceInfoData, state);
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            }

            index++;
        }

        var lastError = Marshal.GetLastWin32Error();
        if (lastError != ErrorNoMoreItems)
        {
            throw new Win32Exception(lastError);
        }

        throw new InvalidOperationException($"Keyboard device '{instanceId}' was not found.");
    }

    private static void ApplyPropChange(SafeDeviceInfoSetHandle deviceInfoSet, ref SetupApiNative.SpDevinfoData deviceInfoData, SetupApiNative.Dics state)
    {
        var header = new SetupApiNative.SpClassinstallHeader
        {
            cbSize = (uint)Marshal.SizeOf<SetupApiNative.SpClassinstallHeader>(),
            InstallFunction = SetupApiNative.Dif.PropertyChange
        };

        var propParams = new SetupApiNative.SpPropchangeParams
        {
            ClassInstallHeader = header,
            StateChange = state,
            Scope = SetupApiNative.DicsFlag.Global,
            HwProfile = 0
        };

        if (!SetupApiNative.SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref propParams, Marshal.SizeOf<SetupApiNative.SpPropchangeParams>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!SetupApiNative.SetupDiCallClassInstaller(SetupApiNative.Dif.PropertyChange, deviceInfoSet, ref deviceInfoData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static (bool IsEnabled, bool IsPresent) GetStatus(uint devInst)
    {
        var result = CfgMgr32Native.CM_Get_DevNode_Status(out var status, out var problem, devInst, 0);
        if (result != CfgMgr32Native.CrSuccess)
        {
            return (true, true);
        }

        var isEnabled = !status.HasFlag(CfgMgr32Native.DevNodeStatus.DnDisabled) && problem != CfgMgr32Native.CmProblem.Disabled;
        var isPresent = !status.HasFlag(CfgMgr32Native.DevNodeStatus.DnRemoved);
        return (isEnabled, isPresent);
    }

    private static string GetInstanceId(SafeDeviceInfoSetHandle deviceInfoSet, ref SetupApiNative.SpDevinfoData deviceInfoData)
    {
        const int ErrorInsufficientBuffer = 122;
        var buffer = new StringBuilder(256);
        if (!SetupApiNative.SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, buffer, buffer.Capacity, out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(error);
            }

            buffer = new StringBuilder(requiredSize);
            if (!SetupApiNative.SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, buffer, buffer.Capacity, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        return buffer.ToString();
    }

    private static string? GetDeviceDescription(SafeDeviceInfoSetHandle deviceInfoSet, ref SetupApiNative.SpDevinfoData deviceInfoData)
    {
        var friendlyName = GetStringProperty(deviceInfoSet, ref deviceInfoData, SetupApiNative.Spdrp.FriendlyName);
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            return friendlyName;
        }

        return GetStringProperty(deviceInfoSet, ref deviceInfoData, SetupApiNative.Spdrp.DevDesc);
    }

    private static IReadOnlyList<string> GetHardwareIds(SafeDeviceInfoSetHandle deviceInfoSet, ref SetupApiNative.SpDevinfoData deviceInfoData)
    {
        var values = GetMultiStringProperty(deviceInfoSet, ref deviceInfoData, SetupApiNative.Spdrp.HardwareId);
        return values.Count == 0
            ? Array.Empty<string>()
            : values;
    }

    private static List<string> GetMultiStringProperty(SafeDeviceInfoSetHandle deviceInfoSet, ref SetupApiNative.SpDevinfoData deviceInfoData, SetupApiNative.Spdrp property)
    {
        if (!SetupApiNative.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, null, 0, out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0 && error != 122)
            {
                return new List<string>();
            }
        }

        if (requiredSize <= 0)
        {
            return new List<string>();
        }

        var buffer = new byte[requiredSize];
        if (!SetupApiNative.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, buffer, buffer.Length, out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                return new List<string>();
            }
        }

        var strings = new List<string>();
        var raw = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        if (string.IsNullOrEmpty(raw))
        {
            return strings;
        }

        foreach (var value in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                strings.Add(value);
            }
        }

        return strings;
    }

    private static string? GetStringProperty(SafeDeviceInfoSetHandle deviceInfoSet, ref SetupApiNative.SpDevinfoData deviceInfoData, SetupApiNative.Spdrp property)
    {
        if (!SetupApiNative.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, null, 0, out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0 && error != 122)
            {
                return null;
            }
        }

        if (requiredSize <= 0)
        {
            return null;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApiNative.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, buffer, buffer.Length, out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                return null;
            }
        }

        var str = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(str) ? null : str;
    }
}
