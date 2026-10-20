using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace aspire_react.Server.Web.ExceptionHandlers;

/// <summary>
/// [FIX-GUID-EMPTY phần 2 — 2026-10-20] Lưới an toàn TOÀN CỤC cho vi phạm tham chiếu của Postgres:
/// <c>23503</c> (foreign_key_violation) và <c>23001</c> (restrict_violation — FK <c>ON DELETE RESTRICT</c>,
/// PostgreSQL dùng mã riêng này khi xóa bản ghi cha còn bị tham chiếu). Trước handler này, mọi chỗ ghi id
/// tham chiếu không tồn tại (hoặc xóa bản ghi còn bị tham chiếu) mà chưa có guard riêng đều nổ ra
/// <b>HTTP 500</b>.
/// <para>
/// Hai nhánh được phân biệt qua SQLSTATE + <see cref="PostgresException.MessageText"/>:
/// <list type="bullet">
///   <item><b>"insert or update on table … violates foreign key constraint"</b> — hàng đang ghi trỏ tới
///     một bản ghi CHA KHÔNG TỒN TẠI ⇒ <b>400</b> <c>REFERENCE_NOT_FOUND</c>.</item>
///   <item><b>"update or delete on table … violates foreign key constraint"</b> — bản ghi CHA đang bị
///     xóa/sửa nhưng vẫn còn hàng CON tham chiếu ⇒ <b>409</b> <c>RESOURCE_IN_USE</c>.</item>
/// </list>
/// </para>
/// <para>
/// KHÔNG ghi đè các xử lý riêng đã có: mọi handler/validator đã trả mã lỗi cụ thể (VD
/// <c>RESOURCE_NOT_FOUND</c>, <c>LOCATION_REQUIRED</c>, <c>INVALID_SUPPLIER</c>, <c>ASSET_ARCHIVED</c>…)
/// sẽ chặn trước khi exception được ném ra; handler này chỉ nhận phần còn sót. Các SQLSTATE khác
/// (<c>23505</c> unique, <c>23514</c> check, …) trả <c>false</c> để pipeline/exception handler khác xử lý.
/// </para>
/// </summary>
public sealed class ForeignKeyViolationExceptionHandler(
    ILogger<ForeignKeyViolationExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Mã lỗi 400: hàng đang ghi trỏ tới bản ghi cha không tồn tại.</summary>
    public const string ReferenceNotFoundCode = "REFERENCE_NOT_FOUND";

    /// <summary>Mã lỗi 409: bản ghi cha đang bị dữ liệu khác tham chiếu (không thể xóa/sửa).</summary>
    public const string ResourceInUseCode = "RESOURCE_IN_USE";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var postgres = FindReferenceViolation(exception);
        if (postgres is null)
            return false;

        // Response đã bắt đầu gửi thì không thể đổi status/body nữa — để pipeline gốc xử lý (log + abort).
        if (httpContext.Response.HasStarted)
            return false;

        var constraint = string.IsNullOrWhiteSpace(postgres.ConstraintName) ? "(unknown)" : postgres.ConstraintName;
        var table = string.IsNullOrWhiteSpace(postgres.TableName) ? "(unknown)" : postgres.TableName;
        // 23001 = restrict_violation (FK khai báo ON DELETE RESTRICT) — PostgreSQL dùng mã RIÊNG này thay
        // cho 23503 khi xóa bản ghi cha còn bị tham chiếu. 23503 nhánh xóa xuất hiện với NO ACTION.
        // 23503 nhánh "insert or update" = hàng đang ghi trỏ tới cha KHÔNG tồn tại.
        var isParentStillReferenced = postgres.SqlState == PostgresErrorCodes.RestrictViolation
            || postgres.MessageText.StartsWith("update or delete", StringComparison.OrdinalIgnoreCase);

        if (isParentStillReferenced)
        {
            logger.LogWarning(
                "Reference violation {SqlState} (delete/update conflict) on table {Table}, constraint {Constraint}: " +
                "bản ghi đang được dữ liệu khác tham chiếu → 409 {Code}. Detail: {Detail}",
                postgres.SqlState, table, constraint, ResourceInUseCode, postgres.MessageText);

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                status = "error",
                message = "Bản ghi đang được sử dụng bởi dữ liệu khác nên không thể xóa hoặc thay đổi.",
                error_code = ResourceInUseCode
            }, cancellationToken);
            return true;
        }

        logger.LogWarning(
            "Reference violation {SqlState} (missing parent) on table {Table}, constraint {Constraint}: " +
            "id tham chiếu không tồn tại → 400 {Code}. Detail: {Detail}",
            postgres.SqlState, table, constraint, ReferenceNotFoundCode, postgres.MessageText);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            status = "error",
            message = "Dữ liệu tham chiếu không tồn tại hoặc đã bị xóa.",
            error_code = ReferenceNotFoundCode
        }, cancellationToken);
        return true;
    }

    /// <summary>
    /// Bóc <see cref="PostgresException"/> khỏi exception (kể cả bọc trong <see cref="DbUpdateException"/>
    /// hoặc <see cref="AggregateException"/>): chấp nhận <c>23503</c> (foreign_key_violation) và
    /// <c>23001</c> (restrict_violation — FK khai báo <c>ON DELETE RESTRICT</c>).
    /// </summary>
    internal static PostgresException? FindReferenceViolation(Exception exception) => exception switch
    {
        PostgresException pg when IsReferenceViolation(pg) => pg,
        DbUpdateException { InnerException: PostgresException inner } when IsReferenceViolation(inner) => inner,
        AggregateException aggregate => aggregate.InnerExceptions
            .Select(FindReferenceViolation)
            .FirstOrDefault(found => found is not null),
        _ => null
    };

    private static bool IsReferenceViolation(PostgresException pg) =>
        pg.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation;
}
