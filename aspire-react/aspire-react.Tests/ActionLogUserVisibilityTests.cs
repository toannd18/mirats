using aspire_react.Server.Application.ActionLogs.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B7a] Quyền xem NHẬT KÝ NGƯỜI DÙNG (`GET /action-logs?itemType=10`):
/// Controller chỉ có `[Authorize]` (ActionLogsController.cs:21) — quyền thực nằm ở handler:
/// <c>GetActionLogsQueryHandler.Handle</c> chặn TƯỜNG MINH `ItemType.User` với non-superuser
/// (trả null → controller 404), TRƯỚC cả nhánh company-scope (vốn coi scope null là "thấy tất cả").
/// Bộ test này khoá hành vi đó: regular user (kể cả FLOATER scope null) và user cùng công ty đều
/// KHÔNG đọc được; chỉ superuser đọc được.
/// </summary>
public class ActionLogUserVisibilityTests
{
    private static AppDbContext NewContext(string name, ICompanyScopeService scope)
        => TestHelpers.CreateContext(name, scope);

    private static async Task<(Guid userId, Guid logId)> SeedUserLogAsync(AppDbContext ctx, Guid? companyId)
    {
        var user = new User
        {
            Username = "log-target",
            Email = "log-target@test.local",
            FirstName = "Log",
            LastName = "Target",
            CompanyId = companyId,
            IsActive = true
        };
        ctx.Users.Add(user);
        var log = new ActionLog
        {
            ItemType = ItemType.User,
            ItemId = user.Id,
            ActionType = ActionType.Update,
            CreatedBy = user.Id,
            CompanyId = companyId,
            Note = "Updated user",
            LogMeta = "{\"changes\":{\"job_title\":{\"old\":null,\"new\":\"QA\"}}}"
        };
        ctx.ActionLogs.Add(log);
        await ctx.SaveChangesAsync();
        return (user.Id, log.Id);
    }

    [Fact]
    public async Task UserLogs_RegularUserSameCompany_CannotRead()
    {
        var companyId = Guid.NewGuid();
        await using var ctx = NewContext(nameof(UserLogs_RegularUserSameCompany_CannotRead),
            new TestHelpers.FakeScope { Super = false, CompanyId = companyId });
        var (userId, _) = await SeedUserLogAsync(ctx, companyId);
        await ctx.SaveChangesAsync();

        var result = await new GetActionLogsQueryHandler(ctx, new TestHelpers.FakeScope { Super = false, CompanyId = companyId })
            .Handle(new GetActionLogsQuery(ItemType.User, userId), CancellationToken.None);

        Assert.Null(result); // controller → 404 (không lộ nhật ký người dùng)
    }

    /// <summary>
    /// Ca nguy hiểm nhất: user KHÔNG thuộc công ty nào (scope null) — nhiều nhánh khác coi scope null
    /// là superuser. Riêng nhật ký User phải chặn, nếu không floater đọc được nhật ký của mọi người.
    /// </summary>
    [Fact]
    public async Task UserLogs_CompanylessNonSuperUser_CannotRead()
    {
        await using var ctx = NewContext(nameof(UserLogs_CompanylessNonSuperUser_CannotRead),
            new TestHelpers.FakeScope { Super = false, CompanyId = null });
        var (userId, _) = await SeedUserLogAsync(ctx, companyId: null);

        var result = await new GetActionLogsQueryHandler(ctx, new TestHelpers.FakeScope { Super = false, CompanyId = null })
            .Handle(new GetActionLogsQuery(ItemType.User, userId), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UserLogs_SuperUser_CanRead()
    {
        await using var ctx = NewContext(nameof(UserLogs_SuperUser_CanRead),
            new TestHelpers.SuperUserScope());
        var (userId, logId) = await SeedUserLogAsync(ctx, companyId: null);

        var result = await new GetActionLogsQueryHandler(ctx, new TestHelpers.SuperUserScope())
            .Handle(new GetActionLogsQuery(ItemType.User, userId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result!, l => l.Id == logId);
        Assert.Contains(result!, l => l.LogMeta != null && l.LogMeta.Contains("job_title"));
    }
}
