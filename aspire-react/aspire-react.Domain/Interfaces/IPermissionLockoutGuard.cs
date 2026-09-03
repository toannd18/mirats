using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// Permission draft used by <see cref="IPermissionLockoutGuard.WouldGroupPermissionEditLockoutAsync"/>.
/// [Giai đoạn 3] Moved verbatim from Infrastructure/Authorization/PermissionLockoutGuard.cs together
/// with the interface extraction — the Application layer consumes the guard through this contract.
/// </summary>
public record GroupPermissionDraft(string PermissionKey, PermissionValue Value);

/// <summary>
/// Ngăn "self-lockout": một Admin (KHÔNG phải Superuser) tự gỡ khả năng quản lý phân quyền
/// (<c>admin</c> / <c>users.edit</c>) của chính mình — qua gán nhóm cho user hoặc sửa permission
/// của nhóm — khi họ là người cuối cùng còn giữ khả năng đó, khiến hệ thống không còn ai
/// có thể sửa được phân quyền nữa.
/// <para>
/// Nguyên tắc (đúng yêu cầu): guard chỉ kích hoạt khi <b>target == actor</b> (user đang thao tác
/// tự gỡ quyền của chính họ — trừ WouldDemoteSuperUserLockoutAsync/WouldDeactivateUserLockoutAsync
/// vốn không giới hạn target). Không chỉ bảo vệ Superuser — Admin thường cũng được bảo vệ nếu
/// họ là người duy nhất còn quyền quản trị. Superuser (flag DB hoặc realm role) luôn được phép.
/// </para>
/// <para>
/// [Giai đoạn 3] Interface extracted verbatim from the concrete
/// <c>Infrastructure/Authorization/PermissionLockoutGuard</c> class so the Groups command handlers
/// (Application) can consume it without referencing Infrastructure — same pattern as
/// ICompanyScopeService/IActionLogVisibilityService. Implementation stays in Infrastructure.
/// </para>
/// </summary>
public interface IPermissionLockoutGuard
{
    /// <summary>
    /// Chuẩn bị gán bộ nhóm mới <paramref name="newGroupIds"/> cho <paramref name="targetUserId"/>.
    /// Trả <c>true</c> nếu thao tác khiến actor (chính là target) mất khả năng quản lý phân quyền
    /// cuối cùng của hệ thống → phải chặn.
    /// </summary>
    Task<bool> WouldSelfAssignLockoutAsync(
        Guid actorId,
        Guid targetUserId,
        IReadOnlyCollection<Guid> newGroupIds,
        bool actorIsRealmSuperUser = false);

    /// <summary>
    /// Chuẩn bị thay thế toàn bộ permission của <paramref name="groupId"/> bằng
    /// <paramref name="newPermissions"/>. Trả <c>true</c> nếu thay đổi khiến actor (thành viên của
    /// group đó) mất khả năng quản lý phân quyền cuối cùng → phải chặn.
    /// </summary>
    Task<bool> WouldGroupPermissionEditLockoutAsync(
        Guid actorId,
        Guid groupId,
        IReadOnlyCollection<GroupPermissionDraft> newPermissions,
        bool actorIsRealmSuperUser = false);

    /// <summary>
    /// Chuẩn bị XÓA <paramref name="groupId"/>. Trả <c>true</c> nếu xóa nhóm này (kèm toàn bộ
    /// <c>GroupPermission</c> và <c>UserGroup</c> trỏ tới nó) khiến hệ thống không còn ai giữ
    /// khả năng quản lý phân quyền (admin / superuser) → phải chặn (ngăn tự khóa quyền quản trị).
    /// </summary>
    Task<bool> WouldDeleteGroupLockoutAsync(
        Guid actorId,
        Guid groupId,
        bool actorIsRealmSuperUser = false);

    /// <summary>
    /// Chuẩn bị hạ cờ superuser (<c>IsSuperUser = false</c>) của <paramref name="targetUserId"/>.
    /// Trả <c>true</c> nếu hạ cờ khiến hệ thống không còn ai giữ khả năng quản lý phân quyền
    /// (admin / superuser) → phải chặn. Guard này KHÔNG giới hạn target == actor: hạ cờ superuser
    /// cuối cùng dù do ai thực hiện cũng đều nguy hiểm như nhau.
    /// </summary>
    Task<bool> WouldDemoteSuperUserLockoutAsync(
        Guid actorId,
        Guid targetUserId,
        bool actorIsRealmSuperUser = false);

    /// <summary>
    /// Chuẩn bị VÔ HIỆU HÓA (soft-deactivate) <paramref name="targetUserId"/>. Trả <c>true</c> nếu
    /// vô hiệu hóa khiến hệ thống không còn ai giữ khả năng quản lý phân quyền (admin / superuser)
    /// → phải chặn. Guard này cũng không giới hạn target == actor.
    /// </summary>
    Task<bool> WouldDeactivateUserLockoutAsync(
        Guid actorId,
        Guid targetUserId,
        bool actorIsRealmSuperUser = false);
}
