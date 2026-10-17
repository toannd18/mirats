using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace aspire_react.Server.Web.ExceptionHandlers;

/// <summary>
/// Maps a <see cref="FluentValidation.ValidationException"/> (thrown by
/// <see cref="Application.Common.Behaviors.ValidationBehavior{TRequest,TResponse}"/>) to a clean
/// 400 response with grouped field errors — the same shape the User controllers already returned
/// from their manual validation. Without this, a validation failure would bubble up as a raw 500.
/// <para>
/// [Bước 0.2 / LL-3] Nếu các lỗi mang ĐÚNG MỘT error code do dự án tự đặt (SCREAMING_SNAKE, VD
/// <c>TIMESTAMP_IN_FUTURE</c> qua <c>WithErrorCode(...)</c>) thì mã đó được phơi ra ở
/// <c>error_code</c> — cùng hợp đồng với các lỗi do handler trả về. Nhiều error code dự án KHÁC NHAU
/// → bỏ trống (mơ hồ, không đoán). Error code mặc định của FluentValidation là tên validator
/// (PascalCase: <c>PredicateValidator</c>, <c>NotNullValidator</c>…) nên luôn bị bỏ qua và KHÔNG bị
/// phơi ra ngoài.
/// </para>
/// </summary>
public sealed class ValidationExceptionHandler : IExceptionHandler
{
    /// <summary>Mã lỗi do dự án tự đặt: chỉ chữ HOA, số và gạch dưới (VD TIMESTAMP_IN_FUTURE).</summary>
    private static readonly Regex ProjectErrorCodePattern =
        new("^[A-Z][A-Z0-9_]{2,}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        var projectCodes = validationException.Errors
            .Select(e => e.ErrorCode)
            .Where(code => !string.IsNullOrWhiteSpace(code) && ProjectErrorCodePattern.IsMatch(code))
            .Distinct()
            .ToArray();

        if (projectCodes.Length == 1)
        {
            await httpContext.Response.WriteAsJsonAsync(new
            {
                status = "error",
                message = "Validation failed.",
                error_code = projectCodes[0],
                errors
            }, cancellationToken);
            return true;
        }

        await httpContext.Response.WriteAsJsonAsync(new
        {
            status = "error",
            message = "Validation failed.",
            errors
        }, cancellationToken);
        return true;
    }
}
