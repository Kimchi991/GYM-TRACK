namespace GymTrackPro.Mobile.Services;

public sealed class LocalDatabaseCompatibilityException : Exception
{
    public LocalDatabaseCompatibilityException(string message)
        : base(message)
    {
    }

    public LocalDatabaseCompatibilityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
