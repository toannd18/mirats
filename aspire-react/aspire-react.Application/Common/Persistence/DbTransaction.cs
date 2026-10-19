using aspire_react.Server.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace aspire_react.Server.Application.Common.Persistence;

/// <summary>
/// [AUDIT-LOG B5] Chạy một khối GHI (dữ liệu + ActionLog) trong CÙNG MỘT transaction, commit đúng 1 lần.
///
/// <para>
/// Trước B5, các handler MaintenanceTemplates/MaintenanceCampaigns ghi theo kiểu 2 bước
/// (<c>SaveChanges</c> dữ liệu → <c>IActionLogService.Log</c> → <c>SaveChanges</c>) KHÔNG bọc transaction:
/// nếu bước ghi log đổ (hoặc tiến trình chết giữa 2 bước) thì dữ liệu đã nằm trong DB mà nhật ký kiểm
/// toán KHÔNG có ⇒ mất truy vết — vi phạm convention §3.2 ("ActionLog cùng transaction với thay đổi").
/// </para>
///
/// <para>
/// Vì Aspire đăng ký Npgsql với <b>retrying execution strategy</b>, transaction do người dùng mở CHỈ
/// hợp lệ khi nằm trong <c>CreateExecutionStrategy().ExecuteAsync(...)</c> (convention Task O/O-FIX) —
/// helper này gói sẵn cả hai để không handler nào quên.
/// </para>
///
/// <para>
/// Delegate nhận chính <see cref="IDbContextTransaction"/> để handler cần savepoint (VD upsert có vòng
/// retry khi đụng unique index) vẫn dùng được; handler thường có thể bỏ qua tham số đó.
/// </para>
/// </summary>
internal static class DbTransaction
{
    internal static async Task ExecuteAsync(
        IApplicationDbContext context,
        Func<IDbContextTransaction, Task> work,
        CancellationToken cancellationToken = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
            await work(tx);
            await tx.CommitAsync(cancellationToken);
        });
    }
}
