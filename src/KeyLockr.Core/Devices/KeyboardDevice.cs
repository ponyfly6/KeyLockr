namespace KeyLockr.Core.Devices;

public sealed record KeyboardDevice(
    string InstanceId,
    string Description,
    IReadOnlyList<string> HardwareIds,
    string? LocationInformation,
    bool IsEnabled,
    bool IsPresent,
    bool IsRemovable);
