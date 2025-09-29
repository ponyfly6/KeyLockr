# KeyLockr

在 Windows 10/11 上一键禁用/启用笔记本内置键盘，同时保证外接键盘可正常输入的开源工具。项目包含命令行工具与系统托盘常驻程序，满足快捷键切换、自动解锁与安全回退等需求。

## 功能亮点

- 🔐 **安全锁定/解锁**：禁用/启用内置键盘，不影响 USB/蓝牙外接键盘。
- 🛡️ **外接键盘保护**：锁定前自动检测外接键盘，必要时弹窗警告并支持倒计时取消。
- ⏱️ **自动解锁**：默认 10 分钟后自动恢复，可配置/关闭，重启后若未启用持久锁定则自动解锁。
- ⚡ **全局快捷键**：默认 `Ctrl+Alt+K` 一键切换，支持在配置文件中自定义。
- 🪟 **托盘陪伴**：系统托盘图标显示状态，左键单击切换，右键菜单支持锁定/解锁/查看状态/打开配置。
- 🛠️ **命令行工具**：提供 `lock` / `unlock` / `status` 命令，支持 `--force` 强制锁定。

## 构建与运行

> ⚠️ **必须在 Windows 上以管理员身份运行**，否则无法禁用设备。

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
2. 克隆项目后执行以下命令生成解决方案：

```bash
cd KeyLockr
# 还原依赖并编译
 dotnet restore
 dotnet build
```

3. 运行命令行工具：

```bash
# 禁用内置键盘（若无外接键盘会自动阻止）
dotnet run --project src/KeyLockr.Cli -- lock

# 强制锁定（风险自负）
dotnet run --project src/KeyLockr.Cli -- lock --force

# 解锁或查询状态
dotnet run --project src/KeyLockr.Cli -- unlock
dotnet run --project src/KeyLockr.Cli -- status

# 调试：列出所有键盘设备及判定
dotnet run --project src/KeyLockr.Cli -- debug
```

4. 打包并运行托盘程序：

```bash
# 生成发布版（会在 bin/Release 下输出 exe）
dotnet publish src/KeyLockr.Tray -c Release -r win-x64 --self-contained false
```

发布后启动 `KeyLockr.Tray.exe`，托盘图标即会出现。左键单击在锁定/解锁之间切换，右键可访问更多菜单。

## 配置文件

配置文件位于 `%APPDATA%\KeyLockr\config.json`。首次打开配置菜单会自动生成一份示例。默认结构如下：

```json
{
  "internalDeviceInstanceIds": [],
  "internalHardwareIdPrefixes": [
    "ACPI\\PNP0303",
    "ACPI\\VEN_",
    "HID\\VID_06CB",
    "HID\\VID_17EF",
    "HID\\VID_04F3"
  ],
  "requireExternalKeyboard": true,
  "autoUnlockTimeoutMinutes": 10,
  "globalHotkey": "Ctrl+Alt+K",
  "persistentLock": false
}
```

- `internalDeviceInstanceIds`：可手动指定被视为内置键盘的设备 InstanceId。
- `internalHardwareIdPrefixes`：用于识别内置键盘的硬件 ID 前缀。
- `requireExternalKeyboard`：是否在锁定前必须检测到外接键盘。
- `autoUnlockTimeoutMinutes`：自动解锁倒计时（分钟）。设置为 0 或负数可关闭自动解锁。
- `globalHotkey`：托盘程序的全局快捷键，格式如 `Ctrl+Alt+K` / `Win+Shift+F12`。
- `persistentLock`：为 `true` 时重启后保持锁定；否则启动时会自动解锁。

> 修改配置文件后数秒内托盘程序会自动加载新配置，并重新注册快捷键/重建定时器。

## 安全机制

- **外接键盘检测**：禁用前确认存在外接键盘，否则会弹窗告警并默认在倒计时结束后取消操作；命令行可通过 `--force` 忽略。
- **自动解锁**：默认 10 分钟后恢复，防止意外锁死。托盘菜单里手动解锁会立即停止计时。
- **紧急恢复**：随时可以运行 `laptopkb unlock`（或托盘菜单“解锁”）来恢复输入。

## 目录结构

```
KeyLockr.sln
src/
  KeyLockr.Core/      # 设备识别与禁用/启用核心逻辑
  KeyLockr.Cli/       # 命令行入口 (lock/unlock/status)
  KeyLockr.Tray/      # Windows Forms 托盘程序
```

## 开发计划（建议）

1. Soft block：提供 Raw Input 级别的“软屏蔽”模式，适用于无管理员权限场景。
2. 开机自启：允许用户在托盘菜单中开启/关闭自启。
3. 内置键盘手动标记：提供 UI 界面列出设备并允许一键标记。
4. 更丰富的状态提示（如倒计时、日志记录、Toast 提醒）。
5. 完善测试：在真实设备上验证更多品牌型号的识别匹配逻辑。

## 注意

- 本仓库在 Linux 环境下编写，未经过真实 Windows 设备实测，请在测试机上验证后再部署到生产环境。
- 禁用设备需管理员权限，若收到“权限不足”提示，请以管理员身份运行命令行或托盘程序。
- 某些品牌的内置键盘硬件 ID 可能与默认列表不一致，可通过 `config.json` 手动补充。

欢迎 Issue / PR，一起把 KeyLockr 打磨成稳定的联想拯救者键盘锁定神器 ✨
