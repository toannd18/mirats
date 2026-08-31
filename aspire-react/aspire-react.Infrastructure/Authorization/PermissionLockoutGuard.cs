using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Authorization;

/// <summary>
/// Permission draft used by <see cref="PermissionLockoutGuard.WouldGroupPermissionEditLockoutAsync"/>.
/// </summary>
public record GroupPermissionDraft(string PermissionKey, PermissionValue Value);

/// <summary>
/// Ngăn "self-lockout": một Admin (KHÔNG phải Superuser) tự gỡ khả năng quản lý phân quyền
/// (<c>admin</c> / <c>users.edit</c>) của chính mình — qua gán nhóm cho user hoặc sửa permission
/// của nhóm — khi họ là người cuối cùng còn giữ khả năng đó, khiến hệ thống không còn ai
/// có thể sửa được phân quyền nữa.
/// <para>
/// Nguyên tắc (đúng yêu cầu): guard chỉ kích hoạt khi <b>target == actor</b> (user đang thao tác
/// tự gỡ quyền của chính họ). Không chỉ bảo vệ Superuser — Admin thường cũng được bảo vệ nếu
/// họ là người duy nhất còn quyền quản trị. Superuser (flag DB hoặc realm role) luôn được phép.
/// </para>
/// </summary>
public class PermissionLockoutGuard
{
    /// <summary>
    /// Chỉ `admin` được tính là "khả năng quản lý phân quyền".
    /// Cả hai endpoint được guard bảo vệ (`PUT /users/{id}/groups` và `PUT /groups/{id}/permissions`)
    /// đều khai báo policy `admin` — `users.edit` KHÔNG mở được quyền gọi lại 2 API này. Coi
    /// `users.edit` là tiêu chí an toàn sẽ tạo lỗ hổng: actor mất `admin` nhưng còn `users.edit`
    /// vẫn bị guard cho là an toàn để gỡ quyền của chính mình, trong khi thực tế họ không thể
    /// gọi lại API vừa dùng để khôi phục quyền.
    /// <para>
    /// Nếu tương lai tách permission riêng (VD `groups.manage`) thay vì dùng chung `admin` wildcard
    /// cho 2 endpoint này, phải cập nhật danh sách key này theo tương ứng.
    /// </para>
    /// </summary>
    private static readonly string[] ManagementKeys = { "admin" };

    private readonly AppDbContext _db;

    public PermissionLockoutGuard(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Chuẩn bị gán bộ nhóm mới <paramref name="newGroupIds"/> cho <paramref name="targetUserId"/>.
    /// Trả <c>true</c> nếu thao tác khiến actor (chính là target) mất khả năng quản lý phân quyền
    /// cuối cùng của hệ thống → phải chặn.
    /// </summary>
    public async Task<bool> WouldSelfAssignLockoutAsync(
        Guid actorId,
        Guid targetUserId,
        IReadOnlyCollection<Guid> newGroupIds,
        bool actorIsRealmSuperUser = false)
    {
        // Chỉ chặn khi user đang thao tác TỰ gỡ quyền của CHÍNH họ.
        if (targetUserId != actorId) return false;

        var actor = await LoadActorAsync(actorId);
        if (actor == null || actor.IsSuperUser || actorIsRealmSuperUser) return false;

        // Khả năng của actor SAU thay đổi: nhóm mới + UserPermission trực tiếp (giữ nguyên).
        if (await HasManagementCapabilityAfterAsync(actor, newGroupIds)) return false;

        // Actor mất khả năng → hệ thống còn ai khác giữ khả năng không?
        return !await AnyOtherUserHasManagementCapabilityAsync(actorId);
    }

    /// <summary>
    /// Chuẩn bị thay thế toàn bộ permission của <paramref name="groupId"/> bằng
    /// <paramref name="newPermissions"/>. Trả <c>true</c> nếu thay đổi khiến actor (thành viên của
    /// group đó) mất khả năng quản lý phân quyền cuối cùng → phải chặn.
    /// </summary>
    public async Task<bool> WouldGroupPermissionEditLockoutAsync(
        Guid actorId,
        Guid groupId,
        IReadOnlyCollection<GroupPermissionDraft> newPermissions,
        bool actorIsRealmSuperUser = false)
    {
        var actor = await LoadActorAsync(actorId);
        if (actor == null || actor.IsSuperUser || actorIsRealmSuperUser) return false;

        // Khả năng của actor SAU khi groupId nhận bộ permission mới.
        if (await HasManagementCapabilityAfterGroupEditAsync(actor, groupId, newPermissions)) return false;

        // Quan trọng: các user khác cũng thuộc group này sẽ bị ảnh hưởng bởi thay đổi —
        // phải tính khả năng của họ với bộ permission MỚI của group.
        var newGroupPermissionEntities = newPermissions
            .Select(p => new GroupPermission { GroupId = groupId, PermissionKey = p.PermissionKey, Value = p.Value })
            .ToList();

        return !await AnyOtherUserHasManagementCapabilityAsync(actorId, groupId, newGroupPermissionEntities);
    }

    /// <summary>
    /// Chuẩn bị XÓA <paramref name="groupId"/>. Trả <c>true</c> nếu xóa nhóm này (kèm toàn bộ
    /// <c>GroupPermission</c> và <c>UserGroup</c> trỏ tới nó) khiến hệ thống không còn ai giữ
    /// khả năng quản lý phân quyền (admin / superuser) → phải chặn (ngăn tự khóa quyền quản trị).
    /// </summary>
    public async Task<bool> WouldDeleteGroupLockoutAsync(
        Guid actorId,
        Guid groupId,
        bool actorIsRealmSuperUser = false)
    {
        var actor = await LoadActorAsync(actorId);
        if (actor == null || actor.IsSuperUser || actorIsRealmSuperUser) return false;

        // Nhóm không cấp quyền quản trị thì xóa không thể gây lockout.
        var grantsAdmin = await _db.GroupPermissions.AsNoTracking()
            .AnyAsync(gp => gp.GroupId == groupId
                && ManagementKeys.Contains(gp.PermissionKey)
                && gp.Value == PermissionValue.Grant);
        if (!grantsAdmin) return false;

        return !await AnyUserHasManagementCapabilityAfterAsync(
            excludedUserId: null,
            nonSuperUserId: null,
            removedGroupId: groupId);
    }

    /// <summary>
    /// Chuẩn bị hạ cờ superuser (<c>IsSuperUser = false</c>) của <paramref name="targetUserId"/>.
    /// Trả <c>true</c> nếu hạ cờ khiến hệ thống không còn ai giữ khả năng quản lý phân quyền
    /// (admin / superuser) → phải chặn. Guard này KHÔNG giới hạn target == actor: hạ cờ superuser
    /// cuối cùng dù do ai thực hiện cũng đều nguy hiểm như nhau.
    /// </summary>
    public async Task<bool> WouldDemoteSuperUserLockoutAsync(
        Guid actorId,
        Guid targetUserId,
        bool actorIsRealmSuperUser = false)
    {
        // Realm superuser giữ quyền tuyệt đối qua role (không phụ thuộc DB flag) → không lockout được.
        if (actorIsRealmSuperUser) return false;

        var target = await LoadUserAsync(targetUserId);
        if (target == null || !target.IsSuperUser) return false; // chỉ guard khi hạ cờ superuser

        return !await AnyUserHasManagementCapabilityAfterAsync(
            excludedUserId: null,
            nonSuperUserId: targetUserId, // target bị hạ cờ → mất quyền superuser (admin qua group vẫn tính)
            removedGroupId: null);
    }

    /// <summary>
    /// Chuẩn bị VÔ HIỆU HÓA (soft-deactivate) <paramref name="targetUserId"/>. Trả <c>true</c> nếu
    /// vô hiệu hóa khiến hệ thống không còn ai giữ khả năng quản lý phân quyền (admin / superuser)
    /// → phải chặn. Guard này cũng không giới hạn target == actor.
    /// </summary>
    public async Task<bool> WouldDeactivateUserLockoutAsync(
        Guid actorId,
        Guid targetUserId,
        bool actorIsRealmSuperUser = false)
    {
        if (actorIsRealmSuperUser) return false;

        var target = await LoadUserAsync(targetUserId);
        if (target == null) return false;

        var targetGroupPerms = target.UserGroups.SelectMany(ug => ug.Group.GroupPermissions);
        var targetHasCapability = target.IsSuperUser
            || ManagementKeys.Any(key => HasEffectiveKey(target.UserPermissions, targetGroupPerms, key));
        if (!targetHasCapability) return false; // vô hiệu hóa người không nắm quyền quản trị → vô hại

        return !await AnyUserHasManagementCapabilityAfterAsync(
            excludedUserId: targetUserId, // user bị vô hiệu hóa → mất hoàn toàn khả năng (không đăng nhập được)
            nonSuperUserId: null,
            removedGroupId: null);
    }


    // ==================== Helpers ====================

    private async Task<User?> LoadActorAsync(Guid actorId)
    {
        return await _db.Users.AsNoTracking()
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == actorId);
    }

    private async Task<User?> LoadUserAsync(Guid userId)
    {
        return await _db.Users.AsNoTracking()
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group).ThenInclude(g => g.GroupPermissions)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    /// <summary>
    /// Kiểm tra trên toàn hệ thống xem sau một biến đổi, có còn user nào giữ khả năng quản lý
    /// phân quyền (superuser flag hoặc admin hiệu dụng) hay không. Các tham số mô tả biến đổi:
    /// <list type="bullet">
    /// <item><paramref name="excludedUserId"/>: user bị loại hoàn toàn khỏi tính toán (VD vô hiệu hóa — không đăng nhập được).</item>
    /// <item><paramref name="nonSuperUserId"/>: user này mất cờ superuser (VD bị hạ cờ) nhưng quyền admin qua group vẫn tính.</item>
    /// <item><paramref name="removedGroupId"/>: nhóm bị xóa → mọi <c>UserGroup</c>/<c>GroupPermission</c> của nó biến mất.</item>
    /// </list>
    /// </summary>
    private async Task<bool> AnyUserHasManagementCapabilityAfterAsync(
        Guid? excludedUserId,
        Guid? nonSuperUserId,
        Guid? removedGroupId)
    {
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group).ThenInclude(g => g.GroupPermissions)
            .ToListAsync();

        foreach (var u in users)
        {
            if (excludedUserId.HasValue && u.Id == excludedUserId.Value) continue;

            var groupPerms = removedGroupId.HasValue
                ? u.UserGroups.Where(ug => ug.GroupId != removedGroupId.Value).SelectMany(ug => ug.Group.GroupPermissions)
                : u.UserGroups.SelectMany(ug => ug.Group.GroupPermissions);

            var isSuper = u.IsSuperUser
                && !(nonSuperUserId.HasValue && u.Id == nonSuperUserId.Value);

            if (isSuper || ManagementKeys.Any(key => HasEffectiveKey(u.UserPermissions, groupPerms, key)))
                return true;
        }

        return false;
    }

    private async Task<bool> HasManagementCapabilityAfterAsync(User actor, IReadOnlyCollection<Guid>? newGroupIds)
    {
        var groupIds = newGroupIds?.Distinct().ToList() ?? new List<Guid>();
        var groupPerms = groupIds.Count == 0
            ? new List<GroupPermission>()
            : await _db.GroupPermissions.AsNoTracking()
                .Where(gp => groupIds.Contains(gp.GroupId))
                .ToListAsync();

        return ManagementKeys.Any(key => HasEffectiveKey(actor.UserPermissions, groupPerms, key));
    }

    private async Task<bool> HasManagementCapabilityAfterGroupEditAsync(
        User actor,
        Guid groupId,
        IReadOnlyCollection<GroupPermissionDraft> newPermissions)
    {
        // Group đang sửa được thay bằng bộ permission mới; các group khác giữ nguyên.
        var otherGroupPerms = await _db.GroupPermissions.AsNoTracking()
            .Where(gp => gp.GroupId != groupId)
            .ToListAsync();

        var groupPerms = otherGroupPerms
            .Concat(newPermissions.Select(p => new GroupPermission
            {
                GroupId = groupId,
                PermissionKey = p.PermissionKey,
                Value = p.Value
            }))
            .ToList();

        return ManagementKeys.Any(key => HasEffectiveKey(actor.UserPermissions, groupPerms, key));
    }

    /// <summary>
    /// Tính quyền hiệu dụng cho một key theo đúng semantic của <c>PermissionHandler</c>:
    /// UserPermission.Deny override mọi thứ → Grant trực tiếp → Group Grant → mặc định không có.
    /// </summary>
    private static bool HasEffectiveKey(
        IEnumerable<UserPermission> userPerms,
        IEnumerable<GroupPermission> groupPerms,
        string key)
    {
        var direct = userPerms.FirstOrDefault(p => p.PermissionKey == key);
        if (direct?.Value == PermissionValue.Deny) return false;
        if (direct?.Value == PermissionValue.Grant) return true;
        return groupPerms.Any(gp => gp.PermissionKey == key && gp.Value == PermissionValue.Grant);
    }

    private async Task<bool> AnyOtherUserHasManagementCapabilityAsync(
        Guid actorId,
        Guid? modifiedGroupId = null,
        IReadOnlyCollection<GroupPermission>? modifiedGroupPermissions = null)
    {
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group).ThenInclude(g => g.GroupPermissions)
            .Where(u => u.Id != actorId)
            .ToListAsync();

        foreach (var u in users)
        {
            if (u.IsSuperUser) return true;

            var groupPerms = EffectiveGroupPermissions(u, modifiedGroupId, modifiedGroupPermissions).ToList();
            if (ManagementKeys.Any(key => HasEffectiveKey(u.UserPermissions, groupPerms, key))) return true;
        }

        return false;
    }

    /// <summary>
    /// Gom toàn bộ GroupPermission hiệu dụng của user, cho phép override permission của một
    /// group đang bị sửa bằng bộ permission mới (các user khác trong group cũng bị ảnh hưởng).
    /// </summary>
    private static IEnumerable<GroupPermission> EffectiveGroupPermissions(
        User user,
        Guid? modifiedGroupId,
        IReadOnlyCollection<GroupPermission>? modifiedGroupPermissions)
    {
        foreach (var ug in user.UserGroups)
        {
            if (ug.GroupId == modifiedGroupId && modifiedGroupPermissions != null)
            {
                foreach (var gp in modifiedGroupPermissions) yield return gp;
            }
            else
            {
                foreach (var gp in ug.Group.GroupPermissions) yield return gp;
            }
        }
    }
}
