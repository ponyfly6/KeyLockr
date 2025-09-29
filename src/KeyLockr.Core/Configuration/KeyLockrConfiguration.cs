using System.Text.Json.Serialization;

namespace KeyLockr.Core.Configuration;

public sealed class KeyLockrConfiguration
{
    private static readonly string[] DefaultHardwareIdPrefixes =
    {
        "ACPI\\PNP0303",
        "ACPI\\VEN_",
        "HID\\VID_06CB",
        "HID\\VID_17EF",
        "HID\\VID_04F3"
    };

    public static KeyLockrConfiguration CreateDefault() => new()
    {
        InternalDeviceInstanceIds = new List<string>(),
        InternalHardwareIdPrefixes = DefaultHardwareIdPrefixes.ToList(),
        RequireExternalKeyboard = true,
        AutoUnlockTimeoutMinutes = 10,
        GlobalHotkey = "Ctrl+Alt+K",
        PersistentLock = false
    };

    [JsonPropertyName("internalDeviceInstanceIds")]
    public List<string> InternalDeviceInstanceIds { get; init; } = new();

    [JsonPropertyName("internalHardwareIdPrefixes")]
    public List<string> InternalHardwareIdPrefixes { get; init; } = DefaultHardwareIdPrefixes.ToList();

    [JsonPropertyName("requireExternalKeyboard")]
    public bool RequireExternalKeyboard { get; init; } = true;

    [JsonPropertyName("autoUnlockTimeoutMinutes")]
    public int AutoUnlockTimeoutMinutes { get; init; } = 10;

    [JsonPropertyName("globalHotkey")]
    public string GlobalHotkey { get; init; } = "Ctrl+Alt+K";

    [JsonPropertyName("persistentLock")]
    public bool PersistentLock { get; init; }

    [JsonIgnore]
    public TimeSpan AutoUnlockTimeout => TimeSpan.FromMinutes(Math.Max(1, AutoUnlockTimeoutMinutes));
}
