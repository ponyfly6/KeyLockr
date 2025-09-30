using System.ComponentModel;
using KeyLockr.Core.Configuration;
using KeyLockr.Core.Devices;
using KeyLockr.Core.Exceptions;
using KeyLockr.Core.Services;

namespace KeyLockr.Core;

public sealed class KeyboardManager : IDisposable
{
    private const int ErrorNotDisableable = unchecked((int)0xE0000231);

    private readonly IKeyboardDeviceService _deviceService;
    private readonly KeyLockrConfigurationStore _configurationStore;
    private readonly SoftKeyboardBlocker _softBlocker;

    public KeyboardManager()
        : this(new KeyboardDeviceService(), new KeyLockrConfigurationStore())
    {
    }

    public KeyboardManager(IKeyboardDeviceService deviceService, KeyLockrConfigurationStore configurationStore)
    {
        _deviceService = deviceService;
        _configurationStore = configurationStore;
        _softBlocker = new SoftKeyboardBlocker();
    }

    public async Task LockAsync(bool skipExternalKeyboardCheck = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var devices = await _deviceService.GetKeyboardsAsync(cancellationToken).ConfigureAwait(false);

        var candidates = GetInternalKeyboardCandidates(devices, configuration);
        if (candidates.Count == 0)
        {
            throw new InternalKeyboardNotFoundException("未找到内置键盘，建议在设置中手动标记。");
        }

        if (candidates.All(device => !device.IsEnabled))
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

        var failureDetails = new List<string>();
        var hardwareDisableSucceeded = false;
        Win32Exception? lastWin32Error = null;

        foreach (var candidate in candidates.Where(device => device.IsEnabled))
        {
            try
            {
                await _deviceService.DisableAsync(candidate.InstanceId, cancellationToken).ConfigureAwait(false);
                await PersistInternalKeyboardIdAsync(configuration, candidate.InstanceId, cancellationToken).ConfigureAwait(false);
                hardwareDisableSucceeded = true;
                return;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 5)
            {
                // 无管理员权限时，记录失败并回退到软件阻止，而不是直接抛出
                failureDetails.Add($"设备 {candidate.Description} ({candidate.InstanceId}) 需要管理员权限才能禁用，已回退到软件阻止。");
                lastWin32Error = ex;
            }
            catch (DeviceOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: ErrorNotDisableable })
            {
                failureDetails.Add($"设备 {candidate.Description} ({candidate.InstanceId}) 不支持硬件禁用。");
                lastWin32Error = ex.InnerException as Win32Exception;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNotDisableable)
            {
                failureDetails.Add($"设备 {candidate.Description} ({candidate.InstanceId}) 不支持硬件禁用。");
                lastWin32Error = ex;
            }
            catch (Win32Exception ex)
            {
                failureDetails.Add($"设备 {candidate.Description} ({candidate.InstanceId}) 禁用失败：{ex.Message}");
                lastWin32Error = ex;
            }
            catch (Exception ex)
            {
                failureDetails.Add($"设备 {candidate.Description} ({candidate.InstanceId}) 禁用失败：{ex.Message}");
            }
        }

        // If hardware disable failed for all devices, try software blocking
        if (!hardwareDisableSucceeded && failureDetails.Count > 0)
        {
            try
            {
                _softBlocker.StartBlocking();
                // Success with software blocking - persist the first candidate's ID for status tracking
                if (candidates.Count > 0)
                {
                    await PersistInternalKeyboardIdAsync(configuration, candidates[0].InstanceId, cancellationToken).ConfigureAwait(false);
                }
                return;
            }
            catch (Exception ex)
            {
                failureDetails.Add($"软件阻止也失败：{ex.Message}");
            }
        }

        if (failureDetails.Count > 0)
        {
            var message = string.Join(Environment.NewLine, failureDetails.Distinct(StringComparer.Ordinal));
            throw lastWin32Error is not null
                ? new DeviceOperationException(message, lastWin32Error)
                : new DeviceOperationException(message, new Exception("禁用设备失败"));
        }
    }

    public async Task UnlockAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var devices = await _deviceService.GetKeyboardsAsync(cancellationToken).ConfigureAwait(false);

        var candidates = GetInternalKeyboardCandidates(devices, configuration, preferDisabled: true);
        if (candidates.Count == 0)
        {
            throw new InternalKeyboardNotFoundException("未找到内置键盘，无法执行解锁。");
        }

        var lockedDevices = candidates.Where(device => !device.IsEnabled).ToList();
        
        // Always stop software blocking first
        if (_softBlocker.IsBlocking)
        {
            _softBlocker.StopBlocking();
        }
        
        if (lockedDevices.Count == 0)
        {
            return;
        }

        var failureDetails = new List<string>();
        Win32Exception? lastWin32Error = null;

        foreach (var device in lockedDevices)
        {
            try
            {
                await _deviceService.EnableAsync(device.InstanceId, cancellationToken).ConfigureAwait(false);
                await PersistInternalKeyboardIdAsync(configuration, device.InstanceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 5)
            {
                throw new AdministrativePrivilegesRequiredException("启用键盘需要管理员权限，请以管理员身份运行应用。");
            }
            catch (Win32Exception ex)
            {
                failureDetails.Add($"设备 {device.Description} ({device.InstanceId}) 启用失败：{ex.Message}");
                lastWin32Error = ex;
            }
        }

        if (failureDetails.Count > 0)
        {
            var message = string.Join(Environment.NewLine, failureDetails.Distinct(StringComparer.Ordinal));
            throw lastWin32Error is not null
                ? new DeviceOperationException(message, lastWin32Error)
                : new DeviceOperationException(message, new Exception("启用设备失败"));
        }
    }

    public async Task<KeyboardStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var devices = await _deviceService.GetKeyboardsAsync(cancellationToken).ConfigureAwait(false);

        var internalKeyboard = GetInternalKeyboardCandidates(devices, configuration).FirstOrDefault();
        if (internalKeyboard == null)
        {
            return KeyboardStatus.Unknown;
        }

        // Check if software blocking is active
        if (_softBlocker.IsBlocking)
        {
            return KeyboardStatus.Locked;
        }
        
        return internalKeyboard.IsEnabled ? KeyboardStatus.Unlocked : KeyboardStatus.Locked;
    }

    private static List<KeyboardDevice> GetInternalKeyboardCandidates(IEnumerable<KeyboardDevice> devices, KeyLockrConfiguration configuration, bool preferDisabled = false)
    {
        return devices
            .Where(device => device.IsPresent)
            .Where(device => CouldBeInternal(device, configuration))
            .Select(device => (Device: device, Score: CalculateDeviceScore(device, configuration, preferDisabled)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Device.IsRemovable)
            .Select(item => item.Device)
            .ToList();
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

    private static bool CouldBeInternal(KeyboardDevice device, KeyLockrConfiguration configuration)
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

        if (device.Description.Contains("PS/2", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !device.IsRemovable;
    }

    private static int CalculateDeviceScore(KeyboardDevice device, KeyLockrConfiguration configuration, bool preferDisabled)
    {
        var score = 0;

        if (preferDisabled && !device.IsEnabled)
        {
            score += 1_000_000;
        }

        if (configuration.InternalDeviceInstanceIds.Any(id => string.Equals(id, device.InstanceId, StringComparison.OrdinalIgnoreCase)))
        {
            score += 500_000;
        }

        if (device.HardwareIds.Any(id => configuration.InternalHardwareIdPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
        {
            score += 250_000;
        }

        if (!string.IsNullOrWhiteSpace(device.LocationInformation) && device.LocationInformation.Contains("internal", StringComparison.OrdinalIgnoreCase))
        {
            score += 50_000;
        }

        if (device.Description.Contains("PS/2", StringComparison.OrdinalIgnoreCase))
        {
            score += 25_000;
        }

        if (!device.IsRemovable)
        {
            score += 10_000;
        }

        if (device.InstanceId.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase))
        {
            score += 5_000;
        }

        if (device.Description.Contains("Keyboard", StringComparison.OrdinalIgnoreCase))
        {
            score += 1_000;
        }

        if (!device.IsEnabled)
        {
            score += 100;
        }

        return score;
    }

    private async Task PersistInternalKeyboardIdAsync(KeyLockrConfiguration configuration, string instanceId, CancellationToken cancellationToken)
    {
        if (configuration.InternalDeviceInstanceIds.Any(id => string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        configuration.InternalDeviceInstanceIds.Add(instanceId);
        try
        {
            await _configurationStore.SaveAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 保存失败不影响锁定主流程，记录失败但不抛出。
        }
    }

    public void Dispose()
    {
        _softBlocker?.Dispose();
    }
}

public enum KeyboardStatus
{
    Unknown,
    Locked,
    Unlocked
}
