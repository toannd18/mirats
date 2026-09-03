using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.SystemInfos.Commands;
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

    // [Giai đoạn 3] SystemInfo migrated to MediatR — guard tests drive the delete handlers
    // directly (real company-scope FakeScope; same MC-7a substance).
    private static DeleteSystemPositionCommandHandler DeletePosHandler(AppDbContext db, bool super, Guid? companyId)
        => new(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId });

    private static DeleteSystemInfoCommandHandler DeleteSysHandler(AppDbContext db, bool super, Guid? companyId)
        => new(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId });

    [Fact]
    public async Task DeletePosition_ReferencedByChecklistItem_BlockedWithClear400()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeletePosition_ReferencedByChecklistItem_BlockedWithClear400));
        var (sysId, posRefId, _, _, _) = await SeedAsync(db);

        var result = await DeletePosHandler(db, true, null)
            .Handle(new DeleteSystemPositionCommand(sysId, posRefId, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("POSITION_IN_USE_BY_CHECKLIST", result.ErrorCode);
        // Row vẫn còn — không bị xóa.
        Assert.True(await db.SystemPositions.AnyAsync(p => p.Id == posRefId));
    }

    [Fact]
    public async Task DeleteSystem_ParentOfReferencedPosition_BlockedWithClear400()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteSystem_ParentOfReferencedPosition_BlockedWithClear400));
        var (sysId, _, _, _, _) = await SeedAsync(db);

        var result = await DeleteSysHandler(db, true, null)
            .Handle(new DeleteSystemInfoCommand(sysId, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("POSITION_IN_USE_BY_CHECKLIST", result.ErrorCode);
        Assert.True(await db.SystemInfos.AnyAsync(s => s.Id == sysId));
    }

    [Fact]
    public async Task DeletePosition_Unreferenced_Allowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeletePosition_Unreferenced_Allowed));
        var (sysId, _, posFreeId, _, _) = await SeedAsync(db);

        var result = await DeletePosHandler(db, true, null)
            .Handle(new DeleteSystemPositionCommand(sysId, posFreeId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(await db.SystemPositions.AnyAsync(p => p.Id == posFreeId));
    }

    [Fact]
    public async Task DeleteSystem_WithoutAnyItemReference_Allowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteSystem_WithoutAnyItemReference_Allowed));
        var (_, _, _, sysFreeId, posFreeSysId) = await SeedAsync(db);

        var result = await DeleteSysHandler(db, true, null)
            .Handle(new DeleteSystemInfoCommand(sysFreeId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(await db.SystemInfos.AnyAsync(s => s.Id == sysFreeId));
        Assert.False(await db.SystemPositions.AnyAsync(p => p.Id == posFreeSysId)); // cascade positions
    }
}