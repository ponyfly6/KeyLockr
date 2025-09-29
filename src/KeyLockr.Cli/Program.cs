using System.CommandLine;
using System.CommandLine.Invocation;
using KeyLockr.Core;
using KeyLockr.Core.Exceptions;

namespace KeyLockr.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var keyboardManager = new KeyboardManager();

        var rootCommand = new RootCommand("KeyLockr - 一键禁用/启用笔记本内置键盘")
        {
            TreatUnmatchedTokensAsErrors = true
        };

        var forceOption = new Option<bool>("--force", "忽略外接键盘检测（风险自负）");

        var lockCommand = new Command("lock", "禁用内置键盘，保护外接键盘不受影响");
        lockCommand.AddOption(forceOption);
        lockCommand.SetHandler(async context =>
        {
            var force = context.ParseResult.GetValueForOption(forceOption);
            await ExecuteAsync(() => keyboardManager.LockAsync(force, context.GetCancellationToken()), context, "已禁用内置键盘。").ConfigureAwait(false);
        });

        var unlockCommand = new Command("unlock", "启用内置键盘");
        unlockCommand.SetHandler(async context =>
        {
            await ExecuteAsync(() => keyboardManager.UnlockAsync(context.GetCancellationToken()), context, "已启用内置键盘。").ConfigureAwait(false);
        });

        var statusCommand = new Command("status", "查看当前内置键盘状态");
        statusCommand.SetHandler(async context =>
        {
            await ExecuteStatusAsync(keyboardManager, context).ConfigureAwait(false);
        });

        var debugCommand = new Command("debug", "调试模式：显示所有检测到的键盘设备");
        debugCommand.SetHandler(async context =>
        {
            await ExecuteDebugAsync(keyboardManager, context).ConfigureAwait(false);
        });

        rootCommand.AddCommand(lockCommand);
        rootCommand.AddCommand(unlockCommand);
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(debugCommand);

        return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(Func<Task> action, InvocationContext context, string successMessage)
    {
        try
        {
            await action().ConfigureAwait(false);
            context.Console.WriteLine(successMessage);
            context.ExitCode = 0;
        }
        catch (ExternalKeyboardNotFoundException ex)
        {
            context.Console.WriteLine(ex.Message);
            context.ExitCode = 2;
        }
        catch (InternalKeyboardNotFoundException ex)
        {
            context.Console.WriteLine(ex.Message);
            context.ExitCode = 3;
        }
        catch (AdministrativePrivilegesRequiredException ex)
        {
            context.Console.WriteLine(ex.Message);
            context.ExitCode = 4;
        }
        catch (KeyLockrException ex)
        {
            context.Console.WriteLine(ex.Message);
            if (ex.InnerException != null)
            {
                context.Console.WriteLine($"详细错误: {ex.InnerException.Message}");
            }
            context.ExitCode = 5;
        }
        catch (OperationCanceledException)
        {
            context.Console.WriteLine("操作已取消。");
            context.ExitCode = 130;
        }
        catch (Exception ex)
        {
            context.Console.WriteLine($"发生未预期的错误：{ex.Message}");
            context.ExitCode = 1;
        }
    }

    private static async Task ExecuteStatusAsync(KeyboardManager keyboardManager, InvocationContext context)
    {
        try
        {
            var status = await keyboardManager.GetStatusAsync(context.GetCancellationToken()).ConfigureAwait(false);
            var message = status switch
            {
                KeyboardStatus.Locked => "状态：已锁定内置键盘",
                KeyboardStatus.Unlocked => "状态：已启用内置键盘",
                _ => "状态：未知，可能未找到内置键盘"
            };

            context.Console.WriteLine(message);
            context.ExitCode = status == KeyboardStatus.Unknown ? 3 : 0;
        }
        catch (OperationCanceledException)
        {
            context.Console.WriteLine("操作已取消。");
            context.ExitCode = 130;
        }
        catch (KeyLockrException ex)
        {
            context.Console.WriteLine(ex.Message);
            context.ExitCode = 5;
        }
        catch (Exception ex)
        {
            context.Console.WriteLine($"发生未预期的错误：{ex.Message}");
            context.ExitCode = 1;
        }
    }

    private static async Task ExecuteDebugAsync(KeyboardManager keyboardManager, InvocationContext context)
    {
        try
        {
            var deviceService = new KeyLockr.Core.Devices.KeyboardDeviceService();
            var configurationStore = new KeyLockr.Core.Configuration.KeyLockrConfigurationStore();
            var configuration = await configurationStore.LoadAsync(context.GetCancellationToken()).ConfigureAwait(false);
            var devices = await deviceService.GetKeyboardsAsync(context.GetCancellationToken()).ConfigureAwait(false);

            context.Console.WriteLine($"检测到 {devices.Count} 个键盘设备：");
            context.Console.WriteLine("");

            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                context.Console.WriteLine($"设备 {i + 1}:");
                context.Console.WriteLine($"  描述: {device.Description}");
                context.Console.WriteLine($"  实例ID: {device.InstanceId}");
                context.Console.WriteLine($"  位置: {device.LocationInformation ?? "未知"}");
                context.Console.WriteLine($"  硬件ID: {string.Join(", ", device.HardwareIds)}");
                context.Console.WriteLine($"  状态: {(device.IsEnabled ? "启用" : "禁用")} | {(device.IsPresent ? "存在" : "不存在")} | {(device.IsRemovable ? "可移除" : "不可移除")}");
                
                // 检查是否被识别为内置键盘
                var isInternal = IsInternalKeyboard(device, configuration);
                context.Console.WriteLine($"  被识别为: {(isInternal ? "内置键盘" : "外接键盘")}");
                context.Console.WriteLine("");
            }

            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            context.Console.WriteLine($"调试失败：{ex.Message}");
            context.ExitCode = 1;
        }
    }

    private static bool IsInternalKeyboard(KeyLockr.Core.Devices.KeyboardDevice device, KeyLockr.Core.Configuration.KeyLockrConfiguration configuration)
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
