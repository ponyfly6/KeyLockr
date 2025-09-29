using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KeyLockr.Core;
using KeyLockr.Core.Configuration;
using KeyLockr.Core.Exceptions;
using KeyLockr.Tray.Interop;

namespace KeyLockr.Tray;

[SupportedOSPlatform("windows")]
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SynchronizationContext _syncContext;
    private readonly NotifyIcon _notifyIcon;
    private readonly KeyboardManager _keyboardManager;
    private readonly KeyLockrConfigurationStore _configurationStore;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly string _configPath;

    private readonly ToolStripMenuItem _toggleMenuItem;
    private readonly ToolStripMenuItem _lockMenuItem;
    private readonly ToolStripMenuItem _unlockMenuItem;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _openConfigMenuItem;

    private readonly Icon _lockedIcon = SystemIcons.Shield;
    private readonly Icon _unlockedIcon = SystemIcons.Application;

    private FileSystemWatcher? _configWatcher;
    private System.Threading.Timer? _autoUnlockTimer;
    private KeyLockrConfiguration _configuration;
    private bool _isLocked;
    private bool _disposed;

    public TrayApplicationContext()
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _keyboardManager = new KeyboardManager();
        _configurationStore = new KeyLockrConfigurationStore();
        _configuration = LoadConfiguration();
        _configPath = _configurationStore.ConfigurationPath;

        // Initialize menu items first
        _toggleMenuItem = new ToolStripMenuItem("切换锁定状态", null, async (_, _) => await ToggleAsync().ConfigureAwait(false));
        _lockMenuItem = new ToolStripMenuItem("锁定内置键盘", null, async (_, _) => await LockInternalAsync().ConfigureAwait(false));
        _unlockMenuItem = new ToolStripMenuItem("解锁内置键盘", null, async (_, _) => await UnlockInternalAsync().ConfigureAwait(false));
        _statusMenuItem = new ToolStripMenuItem("查看状态", null, async (_, _) => await ShowStatusAsync().ConfigureAwait(false));
        _openConfigMenuItem = new ToolStripMenuItem("打开配置文件", null, (_, _) => OpenConfiguration());

        _notifyIcon = CreateNotifyIcon();
        _hotkeyWindow = new HotkeyWindow(OnGlobalHotkey);
        RegisterHotkey();
        InitializeConfigurationWatcher();

        ExecuteSafeAsync(() => InitializeStateAsync(), "初始化状态失败");
    }

    private async Task InitializeStateAsync()
    {
        var status = await _keyboardManager.GetStatusAsync().ConfigureAwait(false);
        var autoUnlocked = false;

        if (status == KeyboardStatus.Locked && !_configuration.PersistentLock)
        {
            await _keyboardManager.UnlockAsync().ConfigureAwait(false);
            status = KeyboardStatus.Unlocked;
            autoUnlocked = true;
        }

        _isLocked = status == KeyboardStatus.Locked;
        ApplyStatusToUi(status);

        if (autoUnlocked)
        {
            ShowBalloon("检测到锁定未启用持久模式，已自动恢复内置键盘。", ToolTipIcon.Info);
        }
    }

    private KeyLockrConfiguration LoadConfiguration()
    {
        try
        {
            return _configurationStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ShowError($"加载配置失败，使用默认设置。\n{ex.Message}");
            return KeyLockrConfiguration.CreateDefault();
        }
    }

    private NotifyIcon CreateNotifyIcon()
    {
        var menu = new ContextMenuStrip();

        var exitMenuItem = new ToolStripMenuItem("退出", null, (_, _) => ExitApplication());

        menu.Items.AddRange(new ToolStripItem[]
        {
            _toggleMenuItem,
            _lockMenuItem,
            _unlockMenuItem,
            new ToolStripSeparator(),
            _statusMenuItem,
            _openConfigMenuItem,
            new ToolStripSeparator(),
            exitMenuItem
        });

        var notifyIcon = new NotifyIcon
        {
            Icon = _unlockedIcon,
            Visible = true,
            Text = "KeyLockr: 状态检测中…",
            ContextMenuStrip = menu
        };

        notifyIcon.MouseClick += async (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                await ToggleAsync().ConfigureAwait(false);
            }
        };

        notifyIcon.BalloonTipTitle = "KeyLockr";
        return notifyIcon;
    }

    private void RegisterHotkey()
    {
        try
        {
            if (HotkeyParser.TryParse(_configuration.GlobalHotkey, out var definition, out var error))
            {
                _hotkeyWindow.Register(definition);
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                ShowBalloon(error + "，已回退至默认 Ctrl+Alt+K。", ToolTipIcon.Warning);
                var fallback = new HotkeyDefinition(HotKeyNative.Modifiers.Control | HotKeyNative.Modifiers.Alt, Keys.K);
                _hotkeyWindow.Register(fallback);
            }
        }
        catch (Exception ex)
        {
            ShowBalloon($"全局快捷键注册失败：{ex.Message}", ToolTipIcon.Warning);
        }
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            var status = await _keyboardManager.GetStatusAsync().ConfigureAwait(false);
            _isLocked = status == KeyboardStatus.Locked;
            ApplyStatusToUi(status);
        }
        catch (AdministrativePrivilegesRequiredException ex)
        {
            ShowBalloon(ex.Message, ToolTipIcon.Warning);
        }
        catch (KeyLockrException ex)
        {
            ShowBalloon(ex.Message, ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            ShowBalloon($"获取状态失败：{ex.Message}", ToolTipIcon.Warning);
        }
    }

    private void ApplyStatusToUi(KeyboardStatus status)
    {
        RunOnUiThread(() =>
        {
            switch (status)
            {
                case KeyboardStatus.Locked:
                    _notifyIcon.Icon = _lockedIcon;
                    _notifyIcon.Text = "KeyLockr: 内置键盘已锁定";
                    _toggleMenuItem.Text = "解锁内置键盘";
                    _lockMenuItem.Enabled = false;
                    _unlockMenuItem.Enabled = true;
                    StartAutoUnlockTimer();
                    break;
                case KeyboardStatus.Unlocked:
                    _notifyIcon.Icon = _unlockedIcon;
                    _notifyIcon.Text = "KeyLockr: 内置键盘已启用";
                    _toggleMenuItem.Text = "锁定内置键盘";
                    _lockMenuItem.Enabled = true;
                    _unlockMenuItem.Enabled = false;
                    StopAutoUnlockTimer();
                    break;
                default:
                    _notifyIcon.Icon = _unlockedIcon;
                    _notifyIcon.Text = "KeyLockr: 未知状态";
                    _toggleMenuItem.Text = "切换锁定状态";
                    _lockMenuItem.Enabled = true;
                    _unlockMenuItem.Enabled = true;
                    StopAutoUnlockTimer();
                    break;
            }
        });
    }

    private async Task ToggleAsync()
    {
        if (_isLocked)
        {
            await UnlockInternalAsync().ConfigureAwait(false);
        }
        else
        {
            await LockInternalAsync().ConfigureAwait(false);
        }
    }

    private async Task LockInternalAsync()
    {
        try
        {
            await _keyboardManager.LockAsync(false).ConfigureAwait(false);
            _isLocked = true;
            ApplyStatusToUi(KeyboardStatus.Locked);
            ShowBalloon("已禁用内置键盘", ToolTipIcon.Info);
        }
        catch (ExternalKeyboardNotFoundException ex)
        {
            if (ShowExternalKeyboardWarning(ex.Message))
            {
                await LockForceAsync().ConfigureAwait(false);
            }
        }
        catch (AdministrativePrivilegesRequiredException ex)
        {
            ShowError(ex.Message);
        }
        catch (KeyLockrException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"锁定失败：{ex.Message}");
        }
    }

    private async Task LockForceAsync()
    {
        try
        {
            await _keyboardManager.LockAsync(true).ConfigureAwait(false);
            _isLocked = true;
            ApplyStatusToUi(KeyboardStatus.Locked);
            ShowBalloon("已强制禁用内置键盘", ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            ShowError($"强制锁定失败：{ex.Message}");
        }
    }

    private async Task UnlockInternalAsync()
    {
        try
        {
            await _keyboardManager.UnlockAsync().ConfigureAwait(false);
            _isLocked = false;
            ApplyStatusToUi(KeyboardStatus.Unlocked);
            ShowBalloon("已启用内置键盘", ToolTipIcon.Info);
        }
        catch (AdministrativePrivilegesRequiredException ex)
        {
            ShowError(ex.Message);
        }
        catch (KeyLockrException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"解锁失败：{ex.Message}");
        }
    }

    private async Task ShowStatusAsync()
    {
        var status = await _keyboardManager.GetStatusAsync().ConfigureAwait(false);
        var message = status switch
        {
            KeyboardStatus.Locked => "内置键盘当前已锁定。",
            KeyboardStatus.Unlocked => "内置键盘当前已启用。",
            _ => "无法确定当前状态，可能未识别到内置键盘。"
        };

        ShowBalloon(message, status == KeyboardStatus.Unknown ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private void ShowError(string message)
    {
        RunOnUiThread(() => MessageBox.Show(message, "KeyLockr", MessageBoxButtons.OK, MessageBoxIcon.Warning));
    }

    private void ShowBalloon(string message, ToolTipIcon icon)
    {
        RunOnUiThread(() =>
        {
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.ShowBalloonTip(3000);
        });
    }

    private bool ShowExternalKeyboardWarning(string message)
    {
        var result = DialogResult.None;
        RunOnUiThread(() =>
        {
            using var dialog = new ExternalKeyboardWarningForm(message);
            result = dialog.ShowDialog();
        });

        return result == DialogResult.OK;
    }

    private void RunOnUiThread(Action action)
    {
        if (action == null)
        {
            return;
        }

        if (SynchronizationContext.Current == _syncContext)
        {
            action();
        }
        else
        {
            _syncContext.Post(_ => action(), null);
        }
    }

    private void StartAutoUnlockTimer()
    {
        StopAutoUnlockTimer();

        if (_configuration.PersistentLock)
        {
            return;
        }

        var interval = _configuration.AutoUnlockTimeout;
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        _autoUnlockTimer = new System.Threading.Timer(_ => ExecuteSafeAsync(() => AutoUnlockAsync(), "自动解锁失败"), null, interval, Timeout.InfiniteTimeSpan);
    }

    private async Task AutoUnlockAsync()
    {
        await UnlockInternalAsync().ConfigureAwait(false);
        ShowBalloon("已自动解锁内置键盘", ToolTipIcon.Info);
    }

    private void StopAutoUnlockTimer()
    {
        _autoUnlockTimer?.Dispose();
        _autoUnlockTimer = null;
    }

    private void OpenConfiguration()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_configPath))
            {
                _configurationStore.SaveAsync(_configuration).GetAwaiter().GetResult();
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _configPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"打开配置文件失败：{ex.Message}");
        }
    }

    private void InitializeConfigurationWatcher()
    {
        var directory = Path.GetDirectoryName(_configPath);
        var fileName = Path.GetFileName(_configPath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            return;
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _configWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _configWatcher.Changed += OnConfigurationChanged;
        _configWatcher.Created += OnConfigurationChanged;
        _configWatcher.Renamed += OnConfigurationChanged;
        _configWatcher.EnableRaisingEvents = true;
    }

    private void OnConfigurationChanged(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false);
            await ReloadConfigurationAsync().ConfigureAwait(false);
        });
    }

    private async Task ReloadConfigurationAsync()
    {
        try
        {
            var configuration = await _configurationStore.LoadAsync().ConfigureAwait(false);
            _configuration = configuration;
            RegisterHotkey();
            if (_isLocked)
            {
                StartAutoUnlockTimer();
            }
            await UpdateStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowBalloon($"重新加载配置失败：{ex.Message}", ToolTipIcon.Warning);
        }
    }

    private void OnGlobalHotkey()
    {
        ExecuteSafeAsync(() => ToggleAsync(), "快捷键执行失败");
    }

    private void ExecuteSafeAsync(Func<Task> action, string? errorContext = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var message = string.IsNullOrEmpty(errorContext) ? ex.Message : $"{errorContext}：{ex.Message}";
                ShowError(message);
            }
        });
    }

    private void ExitApplication()
    {
        Dispose(true);
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        if (disposing)
        {
            StopAutoUnlockTimer();
            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Changed -= OnConfigurationChanged;
                _configWatcher.Created -= OnConfigurationChanged;
                _configWatcher.Renamed -= OnConfigurationChanged;
                _configWatcher.Dispose();
            }

            _hotkeyWindow.Unregister();
            _hotkeyWindow.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
