using System.Reflection;
using System.Security.Claims;
using aspire_react.Server.Application.Assets;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Assets.DTOs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [LL-5] Endpoint-level (authorization + HTTP-status) tests for
/// <c>POST /api/v1/assets/{id}/installations/{entryId}/correct</c> — complementary to the Postgres
/// handler tests in <see cref="InstallationCorrectionTests"/> (which prove the ledger write itself).
/// <para>
/// Two layers covered here, both run on InMemory (no Postgres needed):
/// <list type="number">
///   <item><b>403 gate</b> — the action is bound to policy <c>assets.correct</c>; a non-superuser whose
///     ONLY permission is <c>assets.view</c> (via a normal group) is DENIED <c>assets.correct</c>, while
///     a group holding the <c>admin</c> wildcard IS granted it (documented by-design bypass — see
///     <c>PermissionHandler</c> step 6).</item>
///   <item><b>Error-code → HTTP mapping</b> — <c>INSTALLATION_ALREADY_SUPERSEDED</c> must be <b>409</b>
///     (matching docs/API.md + docs/ERROR_CODES.md), like <c>INSTALLATION_PERIOD_OVERLAP</c>; every
///     other ledger code stays 400 (or 404 for the hide-existence codes).</item>
/// </list>
/// </para>
/// </summary>
public class InstallationCorrectionEndpointTests
{
    // ==================== (1) 403 gate — policy assets.correct ====================

    /// <summary>
    /// Lớp HTTP thật sự trả 403: action phải mang <c>[Authorize(Policy = "assets.correct")]</c>.
    /// Khẳng định bằng reflection để một lần "nới" policy (VD gộp vào assets.edit) là test đỏ ngay.
    /// </summary>
    [Fact]
    public void CorrectAction_DeclaresAssetsCorrectPolicy()
    {
        var method = typeof(AssetsController).GetMethod(nameof(AssetsController.CorrectInstallation));
        Assert.NotNull(method);

        var authorize = Assert.Single(method!.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal("assets.correct", authorize.Policy);
        Assert.Equal("assets.correct", PermissionCatalogProbe.AssetsCorrect);
    }

    /// <summary>
    /// User KHÔNG superuser, thành viên một nhóm THƯỜNG chỉ có <c>assets.view</c> → có <c>assets.view</c>
    /// nhưng KHÔNG có <c>assets.correct</c> ⇒ request tới endpoint bị 403 (policy fail, default deny).
    /// </summary>
    [Fact]
    public async Task ViewerGroup_WithOnlyAssetsView_IsDeniedAssetsCorrect()
    {
        await using var db = TestHelpers.CreateContext(nameof(ViewerGroup_WithOnlyAssetsView_IsDeniedAssetsCorrect));
        var user = await SeedUserInGroupAsync(db, "qa-ll5-viewer", ("assets.view", PermissionValue.Grant));

        var principal = NonSuperUserPrincipal(user.Id);
        Assert.True(await EvaluateAsync(db, principal, "assets.view"));
        Assert.False(await EvaluateAsync(db, principal, "assets.correct"));   // → HTTP 403
        Assert.False(await EvaluateAsync(db, principal, "assets.edit"));
    }

    /// <summary>
    /// Quyền cấp TRỰC TIẾP cho user (không qua nhóm) với key <c>assets.correct</c> ⇒ được phép.
    /// Chứng minh route mở đúng cho người có quyền riêng, không chỉ superuser.
    /// </summary>
    [Fact]
    public async Task UserLevelAssetsCorrectGrant_IsAllowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(UserLevelAssetsCorrectGrant_IsAllowed));
        var user = new User { Username = "qa-ll5-corrector", Email = "qa-ll5-corrector@test.local", IsSuperUser = false };
        db.Users.Add(user);
        db.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionKey = "assets.correct", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        var principal = NonSuperUserPrincipal(user.Id);
        Assert.True(await EvaluateAsync(db, principal, "assets.correct"));
    }

    /// <summary>
    /// Bypass CỦA NHÓM ADMIN LÀ THEO THIẾT KẾ: nhóm chỉ cấp quyền <c>admin</c> (KHÔNG cấp tường minh
    /// <c>assets.correct</c>) vẫn cho thành viên gọi được endpoint — vì <c>admin</c> là WILDCARD trong
    /// <c>PermissionHandler</c> (bước 6). Test này khóa lại hành vi đã được chốt (2026-10-18), không phải bug.
    /// </summary>
    [Fact]
    public async Task GroupWithAdminWildcard_IsGrantedAssetsCorrect_ByDesign()
    {
        await using var db = TestHelpers.CreateContext(nameof(GroupWithAdminWildcard_IsGrantedAssetsCorrect_ByDesign));
        var user = await SeedUserInGroupAsync(db, "qa-ll5-admin", ("assets.view", PermissionValue.Grant), ("admin", PermissionValue.Grant));

        // Nhóm KHÔNG cấp tường minh assets.correct — quyền này chỉ đến từ wildcard "admin".
        var groupId = await db.UserGroups.Where(ug => ug.UserId == user.Id).Select(ug => ug.GroupId).SingleAsync();
        Assert.False(await db.GroupPermissions.AnyAsync(gp => gp.GroupId == groupId && gp.PermissionKey == "assets.correct"));

        var principal = NonSuperUserPrincipal(user.Id);
        Assert.True(await EvaluateAsync(db, principal, "assets.correct"));    // wildcard → cho phép (thiết kế)
    }

    /// <summary>
    /// Ngược lại: nhóm KHÔNG cấp <c>admin</c> thì wildcard không kích hoạt — cặp đôi với test trên
    /// để chứng minh kết quả thật sự đến từ quyền <c>admin</c>, không phải "nhóm nào cũng qua".
    /// </summary>
    [Fact]
    public async Task GroupWithoutAdminWildcard_IsStillDenied_EvenWithAssetsView()
    {
        await using var db = TestHelpers.CreateContext(nameof(GroupWithoutAdminWildcard_IsStillDenied_EvenWithAssetsView));
        var user = await SeedUserInGroupAsync(db, "qa-ll5-noAdmin", ("assets.view", PermissionValue.Grant), ("assets.edit", PermissionValue.Grant));

        var principal = NonSuperUserPrincipal(user.Id);
        Assert.True(await EvaluateAsync(db, principal, "assets.edit"));
        Assert.False(await EvaluateAsync(db, principal, "assets.correct"));
    }

    // ==================== (2) error-code → HTTP status mapping ====================

    /// <summary>
    /// Xung đột append-only ⇒ 409 (không phải 400): bản đã superseded + chồng khoảng lắp đặt.
    /// Mọi mã lỗi còn lại giữ 400 (tương thích body cũ).
    /// </summary>
    [Theory]
    [InlineData(InstallationLedger.AlreadySuperseded, 409)]
    [InlineData(InstallationLedger.Overlap, 409)]
    [InlineData(InstallationLedger.EndBeforeStart, 400)]
    [InlineData(InstallationLedger.Inconsistent, 400)]
    [InlineData("CORRECTION_NO_CHANGES", 400)]
    [InlineData("INSTALLATION_CLOSE_USE_CHECKIN", 400)]
    [InlineData("INSTALLATION_OPEN_POSITION_LOCKED", 400)]
    [InlineData("POSITION_NOT_FOUND", 400)]
    public async Task CorrectInstallation_MapsErrorCodeToStatus(string errorCode, int expectedStatus)
    {
        var controller = BuildController(new CorrectionResult(false, "thông điệp", ErrorCode: errorCode));

        var action = await controller.CorrectInstallation(
            Guid.NewGuid(), Guid.NewGuid(),
            new CorrectInstallationRequestDto(null, null, null, "ghi chú", "lý do đính chính đủ dài"));

        var status = action switch
        {
            ObjectResult r => r.StatusCode,
            StatusCodeResult r => r.StatusCode,
            _ => null
        };
        Assert.Equal(expectedStatus, status);
    }

    /// <summary>Asset/bản ghi không tồn tại/ngoài phạm vi ⇒ 404 hide-existence (không có error_code body riêng).</summary>
    [Theory]
    [InlineData("NOT_FOUND")]
    [InlineData("INSTALLATION_ENTRY_NOT_FOUND")]
    public async Task CorrectInstallation_NotFound_MapsTo404(string errorCode)
    {
        var controller = BuildController(new CorrectionResult(false, "không tìm thấy", ErrorCode: errorCode));

        var action = await controller.CorrectInstallation(
            Guid.NewGuid(), Guid.NewGuid(),
            new CorrectInstallationRequestDto(null, null, null, "ghi chú", "lý do đính chính đủ dài"));

        var notFound = Assert.IsType<NotFoundObjectResult>(action);
        Assert.Equal(404, notFound.StatusCode);
    }

    /// <summary>Thành công ⇒ 200 kèm <c>data.new_entry_id</c>/<c>superseded_entry_id</c>.</summary>
    [Fact]
    public async Task CorrectInstallation_Success_Returns200()
    {
        var newId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var controller = BuildController(new CorrectionResult(true, "Đã đính chính lý lịch lắp đặt.", newId, oldId));

        var action = await controller.CorrectInstallation(
            Guid.NewGuid(), oldId,
            new CorrectInstallationRequestDto(null, null, null, "ghi chú", "lý do đính chính đủ dài"));

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(200, ok.StatusCode);
    }

    // ==================== Hạ tầng test ====================

    /// <summary>Probe để test trên so policy thật với catalog (không hardcode chuỗi 2 nơi).</summary>
    private static class PermissionCatalogProbe
    {
        public static readonly string AssetsCorrect =
            aspire_react.Server.Domain.Authorization.PermissionCatalog.All.Single(p => p.Code == "assets.correct").Code;
    }

    private static AssetsController BuildController(CorrectionResult handlerResult)
        => new(
            TestHelpers.CreateContext(Guid.NewGuid().ToString()),
            TestHelpers.StubMediator.Returning(handlerResult),
            new TestHelpers.FakeCurrentUser(),
            new TestHelpers.SuperUserScope());

    private static async Task<bool> EvaluateAsync(AppDbContext db, ClaimsPrincipal principal, string permissionKey)
    {
        var requirement = new PermissionRequirement(permissionKey);
        var authContext = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement }, principal, resource: null);
        var handler = new PermissionHandler(db, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        });
        await handler.HandleAsync(authContext);
        return authContext.HasSucceeded;
    }

    private static ClaimsPrincipal NonSuperUserPrincipal(Guid localUserId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim("local_user_id", localUserId.ToString()),
            // Non-superuser: realm_access must NOT contain the exact roles "admin"/"superuser".
            new Claim("realm_access", "{\"roles\":[\"user\"]}")
        }, "Test"));

    /// <summary>Tạo user KHÔNG superuser + 1 nhóm thường (không system) cấp đúng các permission truyền vào.</summary>
    private static async Task<User> SeedUserInGroupAsync(
        AppDbContext db, string username, params (string Key, PermissionValue Value)[] grants)
    {
        var user = new User { Username = username, Email = $"{username}@test.local", IsSuperUser = false };
        var group = new PermissionGroup { Name = $"LL5 {username}", IsSystem = false };
        db.Users.Add(user);
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();

        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        foreach (var (key, value) in grants)
            db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = key, Value = value });
        await db.SaveChangesAsync();
        return user;
    }
}
