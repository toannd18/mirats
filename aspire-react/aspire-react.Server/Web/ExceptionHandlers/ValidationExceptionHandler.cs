using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace aspire_react.Server.Web.ExceptionHandlers;

/// <summary>
/// Maps a <see cref="FluentValidation.ValidationException"/> (thrown by
/// <see cref="Application.Common.Behaviors.ValidationBehavior{TRequest,TResponse}"/>) to a clean
/// 400 response with grouped field errors — the same shape the User controllers already returned
/// from their manual validation. Without this, a validation failure would bubble up as a raw 500.
/// </summary>
public sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
            return false;

        var errors = validationException.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            status = "error",
            message = "Validation failed.",
            errors
        }, cancellationToken);
        return true;
    }
}
