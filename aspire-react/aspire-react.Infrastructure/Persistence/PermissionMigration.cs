using aspire_react.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Persistence;

/// <summary>
/// Migration dữ liệu cũ (role Keycloak / flag IsSuperUser) → hệ thống nhóm (PermissionGroup).
/// <para>
/// Legacy: user toàn quyền được đánh dấu bằng flag DB <c>IsSuperUser</c> (hoặc realm role
/// superuser/admin). Sau khi có hệ thống nhóm, migration gán các user <c>IsSuperUser = true</c>
/// vào nhóm hệ thống <c>"Superuser"</c> (đã được seed với đủ 76 permission) — để khi về sau bỏ
/// bypass theo flag, quyền hiện có VẪN được bảo toàn qua nhóm.
/// </para>
/// <para>
/// Nguyên tắc an toàn: chỉ THÊM membership, không gỡ gì, không đổi UserPermission/GroupPermission
/// hiện có, idempotent (chạy lại bao nhiêu lần cũng không trùng lặp) → KHÔNG BAO GIỜ thu hẹp
/// quyền hiện có của bất kỳ user nào.
/// </para>
/// </summary>
public static class PermissionMigration
{
    public const string SuperuserGroupName = "Superuser";

    /// <summary>
    /// Gán mọi user có <c>IsSuperUser == true</c> chưa thuộc nhóm "Superuser" vào nhóm đó.
    /// Nếu chưa có nhóm "Superuser" (seed chưa chạy) thì bỏ qua an toàn.
    /// </summary>
    public static async Task AssignLegacySuperUsersToSuperuserGroupAsync(
        AppDbContext db,
        CancellationToken ct = default)
    {
        var superuserGroup = await db.PermissionGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Name == SuperuserGroupName && g.IsSystem, ct);

        if (superuserGroup == null)
            return;

        var legacyUsers = await db.Users
            .Include(u => u.UserGroups)
            .Where(u => u.IsSuperUser && !u.UserGroups.Any(ug => ug.GroupId == superuserGroup.Id))
            .ToListAsync(ct);

        foreach (var user in legacyUsers)
        {
            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = superuserGroup.Id });
        }

        if (legacyUsers.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
