using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// MC-7a — Guard xóa SystemPosition / SystemInfo khi bị ChecklistItem (template bảo dưỡng)
/// tham chiếu: trả 400 POSITION_IN_USE_BY_CHECKLIST (không để lộ 500 FK thô); vị trí/hệ thống
/// KHÔNG bị tham chiếu vẫn xóa bình thường.
/// </summary>
public class MaintenancePositionGuardTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static string? ErrorCode(IActionResult result)
    {
        object? value = result switch
        {
            BadRequestObjectResult bad => bad.Value,
            _ => null
        };
        if (value == null) return null;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJson));
        return doc.RootElement.TryGetProperty("error_code", out var el) ? el.GetString() : null;
    }

    private async Task<(Guid systemId, Guid posRefId, Guid posFreeId, Guid systemFreeId, Guid posFreeSystemId)>
        SeedAsync(AppDbContext db)
    {
        var sys = new SystemInfo { Code = "M7A-2026-001", Name = "Hệ thống bị khóa" };
        var posRef = new SystemPosition { Code = "M7A-2026-101", Name = "Vị trí bị tham chiếu", SystemInfoId = sys.Id };
        var posFree = new SystemPosition { Code = "M7A-2026-102", Name = "Vị trí tự do", SystemInfoId = sys.Id };
        var sysFree = new SystemInfo { Code = "M7A-2026-002", Name = "Hệ thống tự do" };
        var posFreeSys = new SystemPosition { Code = "M7A-2026-203", Name = "Vị trí hệ thống tự do", SystemInfoId = sysFree.Id };

        var tpl = new MaintenanceChecklistTemplate { Name = "T-M7A", SystemInfoId = sys.Id, CreatedById = Guid.NewGuid() };
        var ver = new MaintenanceChecklistTemplateVersion { TemplateId = tpl.Id, VersionNumber = 1, CreatedById = Guid.NewGuid() };
        var item = new MaintenanceChecklistItem { Order = 1, Name = "Hạng mục giới hạn vị trí", CycleMonths = 6 };
        ver.Items.Add(item);
        tpl.Versions.Add(ver);

        db.SystemInfos.AddRange(sys, sysFree);
        db.SystemPositions.AddRange(posRef, posFree, posFreeSys);
        db.MaintenanceChecklistTemplates.Add(tpl);
        await db.SaveChangesAsync();

        // Item khai báo áp dụng ĐÚNG 1 vị trí (posRef) — MC-7a cấu hình template.
        db.MaintenanceChecklistItemPositions.Add(new MaintenanceChecklistItemPosition
        {
            ItemId = item.Id,
            SystemPositionId = posRef.Id
        });
        await db.SaveChangesAsync();

        return (sys.Id, posRef.Id, posFree.Id, sysFree.Id, posFreeSys.Id);
    }

    private static SystemInfoController Ctx(AppDbContext db, bool super, Guid? companyId)
    {
        var controller = new SystemInfoController(
            db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId }, TestHelpers.CreateActionLogService(db));
        // SystemInfoController.GetCurrentUserId() đọc claim "local_user_id" từ HttpContext —
        // cần set ControllerContext cho các hành động ghi ActionLog (Create/Delete).
        var userId = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("local_user_id", userId.ToString()) }, "Test"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task DeletePosition_ReferencedByChecklistItem_BlockedWithClear400()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeletePosition_ReferencedByChecklistItem_BlockedWithClear400));
        var (sysId, posRefId, _, _, _) = await SeedAsync(db);
        var controller = Ctx(db, true, null);

        var result = await controller.DeletePosition(sysId, posRefId);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("POSITION_IN_USE_BY_CHECKLIST", ErrorCode(result));
        // Row vẫn còn — không bị xóa.
        Assert.True(await db.SystemPositions.AnyAsync(p => p.Id == posRefId));
    }

    [Fact]
    public async Task DeleteSystem_ParentOfReferencedPosition_BlockedWithClear400()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteSystem_ParentOfReferencedPosition_BlockedWithClear400));
        var (sysId, _, _, _, _) = await SeedAsync(db);
        var controller = Ctx(db, true, null);

        var result = await controller.Delete(sysId);

        Assert.Equal("POSITION_IN_USE_BY_CHECKLIST", ErrorCode(result));
        Assert.True(await db.SystemInfos.AnyAsync(s => s.Id == sysId));
    }

    [Fact]
    public async Task DeletePosition_Unreferenced_Allowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeletePosition_Unreferenced_Allowed));
        var (sysId, _, posFreeId, _, _) = await SeedAsync(db);
        var controller = Ctx(db, true, null);

        var result = await controller.DeletePosition(sysId, posFreeId);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await db.SystemPositions.AnyAsync(p => p.Id == posFreeId));
    }

    [Fact]
    public async Task DeleteSystem_WithoutAnyItemReference_Allowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteSystem_WithoutAnyItemReference_Allowed));
        var (_, _, _, sysFreeId, posFreeSysId) = await SeedAsync(db);
        var controller = Ctx(db, true, null);

        var result = await controller.Delete(sysFreeId);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await db.SystemInfos.AnyAsync(s => s.Id == sysFreeId));
        Assert.False(await db.SystemPositions.AnyAsync(p => p.Id == posFreeSysId)); // cascade positions
    }
}