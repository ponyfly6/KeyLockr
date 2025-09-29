namespace KeyLockr.Core.Exceptions;

public sealed class InternalKeyboardNotFoundException : KeyLockrException
{
    public InternalKeyboardNotFoundException(string message) : base(message)
    {
    }
}
