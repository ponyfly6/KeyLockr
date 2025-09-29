namespace KeyLockr.Core.Exceptions;

public sealed class ExternalKeyboardNotFoundException : KeyLockrException
{
    public ExternalKeyboardNotFoundException(string message) : base(message)
    {
    }
}
