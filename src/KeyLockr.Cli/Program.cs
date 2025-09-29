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

        rootCommand.AddCommand(lockCommand);
        rootCommand.AddCommand(unlockCommand);
        rootCommand.AddCommand(statusCommand);

        return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(Func<Task> action, InvocationContext context, string successMessage)
    {
        try
        {
            await action().ConfigureAwait(false);
            context.Console.Out.WriteLine(successMessage);
            context.ExitCode = 0;
        }
        catch (ExternalKeyboardNotFoundException ex)
        {
            context.Console.Error.WriteLine(ex.Message);
            context.ExitCode = 2;
        }
        catch (InternalKeyboardNotFoundException ex)
        {
            context.Console.Error.WriteLine(ex.Message);
            context.ExitCode = 3;
        }
        catch (AdministrativePrivilegesRequiredException ex)
        {
            context.Console.Error.WriteLine(ex.Message);
            context.ExitCode = 4;
        }
        catch (KeyLockrException ex)
        {
            context.Console.Error.WriteLine(ex.Message);
            context.ExitCode = 5;
        }
        catch (OperationCanceledException)
        {
            context.Console.Error.WriteLine("操作已取消。");
            context.ExitCode = 130;
        }
        catch (Exception ex)
        {
            context.Console.Error.WriteLine($"发生未预期的错误：{ex.Message}");
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

            context.Console.Out.WriteLine(message);
            context.ExitCode = status == KeyboardStatus.Unknown ? 3 : 0;
        }
        catch (OperationCanceledException)
        {
            context.Console.Error.WriteLine("操作已取消。");
            context.ExitCode = 130;
        }
        catch (KeyLockrException ex)
        {
            context.Console.Error.WriteLine(ex.Message);
            context.ExitCode = 5;
        }
        catch (Exception ex)
        {
            context.Console.Error.WriteLine($"发生未预期的错误：{ex.Message}");
            context.ExitCode = 1;
        }
    }
}
