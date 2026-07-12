using System.Collections.Generic;

namespace GymTrackPro.Shared.DTOs;

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public List<string> Errors { get; set; } = new();

    public static ApiResponse SuccessResponse(string message = "")
    {
        return new ApiResponse { Success = true, Message = message };
    }

    public static ApiResponse FailureResponse(string message, string? errorCode = null, List<string>? errors = null)
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            Errors = errors ?? new List<string>()
        };
    }
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }

    public static ApiResponse<T> SuccessResponse(T data, string message = "")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data
        };
    }

    public static new ApiResponse<T> FailureResponse(string message, string? errorCode = null, List<string>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            Errors = errors ?? new List<string>(),
            Data = default
        };
    }
}
