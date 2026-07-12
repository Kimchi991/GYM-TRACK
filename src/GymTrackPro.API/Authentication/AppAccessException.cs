namespace GymTrackPro.API.Authentication;

public sealed class AppAccessException : Exception
{
    public AppAccessException(int statusCode, string errorCode, string publicMessage)
        : base(publicMessage)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        PublicMessage = publicMessage;
    }

    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string PublicMessage { get; }
}
