using System.Net;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor correlation)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException vex)
        {
            await WriteProblemAsync(context, correlation.CorrelationId, HttpStatusCode.BadRequest,
                "validation_error", "One or more fields are invalid.",
                vex.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }
        catch (DomainException dex)
        {
            _logger.LogWarning(dex, "Domain rule violated: {Code}", dex.Code);
            var status = ErrorCatalog.StatusFor(dex.Code);
            if (dex.Code == "serialization_conflict")
            {
                context.Response.Headers["Retry-After"] = "1";
            }
            await WriteProblemAsync(context, correlation.CorrelationId,
                status, dex.Code, dex.Message, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteProblemAsync(context, correlation.CorrelationId,
                HttpStatusCode.InternalServerError, "internal_error",
                "An unexpected error occurred.", null);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, string correlationId,
        HttpStatusCode status, string code, string title, Dictionary<string, string[]>? errors)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)status;
        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Type = $"https://storeos.io/errors/{code}",
        };
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["code"] = code;
        if (errors != null) problem.Extensions["errors"] = errors;
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}
