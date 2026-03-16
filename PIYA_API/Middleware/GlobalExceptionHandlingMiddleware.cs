using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace PIYA_API.Middleware;

/// <summary>
/// Global exception handling middleware for centralized error responses
/// </summary>
public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        
        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path
        };

        // In development expose full detail; in production return only safe generic messages.
        var isDevelopment = context.RequestServices
            .GetService<IWebHostEnvironment>()?.IsDevelopment() == true;

        switch (exception)
        {
            case UnauthorizedAccessException:
                problemDetails.Status = (int)HttpStatusCode.Unauthorized;
                problemDetails.Title  = "Unauthorized";
                problemDetails.Detail = isDevelopment ? exception.Message : "Access denied.";
                break;

            case ArgumentNullException:
            case ArgumentException:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title  = "Bad Request";
                problemDetails.Detail = isDevelopment ? exception.Message : "The request is invalid.";
                break;

            case KeyNotFoundException:
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title  = "Not Found";
                problemDetails.Detail = isDevelopment ? exception.Message : "The requested resource was not found.";
                break;

            case InvalidOperationException:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title  = "Invalid Operation";
                problemDetails.Detail = isDevelopment ? exception.Message : "The operation could not be completed.";
                break;

            case FileNotFoundException:
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title  = "Not Found";
                problemDetails.Detail = isDevelopment ? exception.Message : "The requested resource was not found.";
                break;

            default:
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Title  = "Internal Server Error";
                problemDetails.Detail = isDevelopment
                    ? exception.Message
                    : "An unexpected error occurred. Please try again later.";
                if (isDevelopment)
                    problemDetails.Extensions["stackTrace"] = exception.StackTrace;
                break;
        }

        context.Response.StatusCode = problemDetails.Status.Value;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, options));
    }
}
