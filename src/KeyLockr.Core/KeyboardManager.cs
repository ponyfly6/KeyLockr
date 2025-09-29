using System.ComponentModel;
using KeyLockr.Core.Configuration;
using KeyLockr.Core.Devices;
using KeyLockr.Core.Exceptions;

namespace KeyLockr.Core;

public sealed class KeyboardManager
{
    private readonly IKeyboardDeviceService _deviceService;
    private readonly KeyLockrConfigurationStore _configurationStore;

    public KeyboardManager()
        : this(new KeyboardDeviceService(), new KeyLockrConfigurationStore())
    {
    }

    public KeyboardManager(IKeyboardDeviceService deviceService, KeyLockrConfigurationStore configurationStore)
    {
        _deviceService = deviceService;
        _configurationStore = configurationStore;
    }

    public async Task LockAsync(bool skipExternalKeyboardCheck = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var devices = await _deviceService.GetKeyboardsAsync(cancellationToken).ConfigureAwait(false);

        var internalKeyboard = SelectInternalKeyboard(devices, configuration)
            ?? throw new InternalKeyboardNotFoundException("未找到内置键盘，建议在设置中手动标记。");

        if (!internalKeyboard.IsEnabled)
        {
            return;
        }

        if (configuration.RequireExternalKeyboard && !skipExternalKeyboardCheck)
        {
            var hasExternal = devices.Any(device => device.IsPresent && device.IsEnabled && !IsInternal(device, configuration));
            if (!hasExternal)
            {
                throw new ExternalKeyboardNotFoundException("未检测到外接键盘。连接外接键盘后再尝试锁定以防止无法输入。");
            }
        }

        try
        {
            await _deviceService.DisableAsync(internalKeyboard.InstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5)
        {
            throw new AdministrativePrivilegesRequiredException("禁用键盘需要管理员权限，请以管理员身份运行应用。");
        }
        catch (Win32Exception ex)
        {
            throw new DeviceOperationException("禁用内置键盘失败，请稍后重试。", ex);
        }
    }

    public async Task UnlockAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var devices = await _deviceService.GetKeyboardsAsync(cancellationToken).ConfigureAwait(false);

        var internalKeyboard = SelectInternalKeyboard(devices, configuration)
            ?? throw new InternalKeyboardNotFoundException("未找到内置键盘，无法执行解锁。");

        if (internalKeyboard.IsEnabled)
        {
            return;
        }

        try
        {
            await _deviceService.EnableAsync(internalKeyboard.InstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5)
        {
            throw new AdministrativePrivilegesRequiredException("启用键盘需要管理员权限，请以管理员身份运行应用。");
        }
        catch (Win32Exception ex)
        {
            throw new DeviceOperationException("启用内置键盘失败，请稍后重试。", ex);
        }
    }

    public async Task<KeyboardStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var devices = await _deviceService.GetKeyboardsAsync(cancellationToken).ConfigureAwait(false);

        var internalKeyboard = SelectInternalKeyboard(devices, configuration);
        if (internalKeyboard == null)
        {
            return KeyboardStatus.Unknown;
        }

        return internalKeyboard.IsEnabled ? KeyboardStatus.Unlocked : KeyboardStatus.Locked;
    }

    private static KeyboardDevice? SelectInternalKeyboard(IEnumerable<KeyboardDevice> devices, KeyLockrConfiguration configuration)
    {
        var list = devices.Where(device => device.IsPresent).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var matchById = list.FirstOrDefault(device => configuration.InternalDeviceInstanceIds.Any(id => string.Equals(id, device.InstanceId, StringComparison.OrdinalIgnoreCase)));
        if (matchById != null)
        {
            return matchById;
        }

        var matchByHardware = list.FirstOrDefault(device => device.HardwareIds.Any(id => configuration.InternalHardwareIdPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))));
        if (matchByHardware != null)
        {
            return matchByHardware;
        }

        var matchByLocation = list.FirstOrDefault(device => !string.IsNullOrWhiteSpace(device.LocationInformation) && device.LocationInformation.Contains("internal", StringComparison.OrdinalIgnoreCase));
        if (matchByLocation != null)
        {
            return matchByLocation;
        }

        var ps2Candidate = list.FirstOrDefault(device => device.Description.Contains("PS/2", StringComparison.OrdinalIgnoreCase));
        if (ps2Candidate != null)
        {
            return ps2Candidate;
        }

        return list.FirstOrDefault(device => !device.IsRemovable);
    }

    private static bool IsInternal(KeyboardDevice device, KeyLockrConfiguration configuration)
    {
        if (configuration.InternalDeviceInstanceIds.Any(id => string.Equals(id, device.InstanceId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (device.HardwareIds.Any(id => configuration.InternalHardwareIdPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(device.LocationInformation) && device.LocationInformation.Contains("internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !device.IsRemovable;
    }
}

public enum KeyboardStatus
{
    Unknown,
    Locked,
    Unlocked
}
