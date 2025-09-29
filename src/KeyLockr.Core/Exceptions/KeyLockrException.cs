namespace KeyLockr.Core.Exceptions;

public class KeyLockrException : Exception
{
    public KeyLockrException(string message) : base(message)
    {
    }

    public KeyLockrException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
