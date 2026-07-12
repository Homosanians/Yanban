using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanban.Application.Common;

namespace Yanban.API.Middleware;

/// <summary>
/// Translates known application exceptions (and EF concurrency conflicts) into
/// RFC 7807 ProblemDetails responses; everything else becomes a 500.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            await WriteProblem(context, ex.StatusCode, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteProblem(context, StatusCodes.Status409Conflict,
                "The resource was modified by someone else. Please reload and retry.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteProblem(context, StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.");
        }
    }

    private static Task WriteProblem(HttpContext context, int statusCode, string detail)
    {
        var problem = new ProblemDetails { Status = statusCode, Title = detail };
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(problem);
    }
}
