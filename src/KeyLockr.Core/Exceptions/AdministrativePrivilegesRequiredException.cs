namespace KeyLockr.Core.Exceptions;

public sealed class AdministrativePrivilegesRequiredException : KeyLockrException
{
    public AdministrativePrivilegesRequiredException(string message) : base(message)
    {
    }
}
