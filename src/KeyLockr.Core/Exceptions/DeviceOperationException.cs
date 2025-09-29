namespace KeyLockr.Core.Exceptions;

public sealed class DeviceOperationException : KeyLockrException
{
    public DeviceOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
