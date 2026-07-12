using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.API.Authentication;

namespace GymTrackPro.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.TraceIdentifier;
            if (ex is AppAccessException accessException)
            {
                // Expected access denials are telemetry, not server faults. Record only the
                // controlled category and correlation handle; never attach request values,
                // token material, invite codes, UID/email, or an exception stack.
                _logger.LogWarning(
                    "Request rejected. StatusCode: {StatusCode}; ErrorCode: {ErrorCode}; CorrelationId: {CorrelationId}",
                    accessException.StatusCode,
                    accessException.ErrorCode,
                    correlationId);
            }
            else if (ex is UnauthorizedAccessException or KeyNotFoundException)
            {
                _logger.LogWarning(
                    "Request rejected. ReasonCategory: {ReasonCategory}; CorrelationId: {CorrelationId}",
                    ex.GetType().Name,
                    correlationId);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Request failed. CorrelationId: {CorrelationId}",
                    correlationId);
            }

            await HandleExceptionAsync(context, ex, correlationId, _environment.IsDevelopment());
        }
    }

    private static Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        string correlationId,
        bool includeDetails)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        
        var statusCode = HttpStatusCode.InternalServerError;
        var message = "An internal server error occurred.";
        string? errorCode = "INTERNAL_ERROR";
        var errors = new List<string>();

        if (exception is AppAccessException accessException)
        {
            statusCode = (HttpStatusCode)accessException.StatusCode;
            message = accessException.PublicMessage;
            errorCode = accessException.ErrorCode;
        }
        else if (exception is UnauthorizedAccessException)
        {
            statusCode = HttpStatusCode.Forbidden;
            message = "Access is forbidden.";
            errorCode = "ACCESS_FORBIDDEN";
            errors.Add(message);
        }
        else if (exception is KeyNotFoundException)
        {
            statusCode = HttpStatusCode.NotFound;
            message = "The requested resource was not found.";
            errorCode = "RESOURCE_NOT_FOUND";
        }
        else if (includeDetails)
        {
            errors.Add(exception.Message);
        }
        else
        {
            // Production responses contain only a request correlation handle. Full details stay in logs.
            errors.Add($"Correlation ID: {correlationId}");
        }

        context.Response.StatusCode = (int)statusCode;

        var response = ApiResponse.FailureResponse(message, errorCode, errors);
        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return context.Response.WriteAsync(jsonResponse);
    }
}
