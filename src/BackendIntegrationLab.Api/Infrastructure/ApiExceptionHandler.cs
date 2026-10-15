using BackendIntegrationLab.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BackendIntegrationLab.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            BankingRuleException => (400, "Business rule rejected the request"),
            AccountNotFoundException => (404, "Account not found"),
            _ => (500, "An unexpected server error occurred")
        };
        if (status == 500)
            logger.LogError(exception, "Request failed; any uncommitted transaction has been rolled back.");
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == 500 ? "The operation could not be completed." : exception.Message,
            Instance = context.Request.Path
        }, options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
