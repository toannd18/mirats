using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [Giai đoạn 3 — Groups] WIRE TESTS: chứng minh các Group command handlers THỰC SỰ GỌI
/// IPermissionLockoutGuard với đúng tham số — độc lập với việc "guard logic đúng"
/// (PermissionLockoutTests/TaskJ đã phủ logic của guard class). 2 phép chứng minh khác nhau:
/// guard CÓ ĐƯỢC GỌI trong luồng mới (ở đây, qua spy) + guard logic đúng (các test guard-level).
/// </summary>
public class GroupLockoutWireTests
{
    /// <summary>Spy ghi lại mọi lần guard được gọi — không chứa logic lockout thật.</summary>
    private sealed class SpyLockoutGuard : IPermissionLockoutGuard
    {
        public int SelfAssignCalls;
        public int GroupPermissionEditCalls;
        public int DeleteCalls;
        public int DemoteCalls;
        public int DeactivateCalls;

        public (Guid ActorId, Guid GroupId, bool RealmSuper) LastDeleteArgs;
        public (Guid ActorId, Guid GroupId, int DraftCount, bool RealmSuper) LastPermEditArgs;

        /// <summary>Kết quả giả lập: true = guard báo lockout (handler phải chặn).</summary>
        public bool LockoutResult { get; set; }

        public Task<bool> WouldSelfAssignLockoutAsync(Guid actorId, Guid targetUserId, IReadOnlyCollection<Guid> newGroupIds, bool actorIsRealmSuperUser = false)
        { SelfAssignCalls++; return Task.FromResult(LockoutResult); }

        public Task<bool> WouldGroupPermissionEditLockoutAsync(Guid actorId, Guid groupId, IReadOnlyCollection<GroupPermissionDraft> newPermissions, bool actorIsRealmSuperUser = false)
        { GroupPermissionEditCalls++; LastPermEditArgs = (actorId, groupId, newPermissions.Count, actorIsRealmSuperUser); return Task.FromResult(LockoutResult); }

        public Task<bool> WouldDeleteGroupLockoutAsync(Guid actorId, Guid groupId, bool actorIsRealmSuperUser = false)
        { DeleteCalls++; LastDeleteArgs = (actorId, groupId, actorIsRealmSuperUser); return Task.FromResult(LockoutResult); }

        public Task<bool> WouldDemoteSuperUserLockoutAsync(Guid actorId, Guid targetUserId, bool actorIsRealmSuperUser = false)
        { DemoteCalls++; return Task.FromResult(LockoutResult); }

        public Task<bool> WouldDeactivateUserLockoutAsync(Guid actorId, Guid targetUserId, bool actorIsRealmSuperUser = false)
        { DeactivateCalls++; return Task.FromResult(LockoutResult); }
    }

    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    [Fact]
    public async Task DeleteGroupHandler_CallsGuard_WithActorIdGroupIdAndSuperFlag()
    {
        await using var db = CreateContext(nameof(DeleteGroupHandler_CallsGuard_WithActorIdGroupIdAndSuperFlag));
        var group = new PermissionGroup { Name = "WireTarget" };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();

        var spy = new SpyLockoutGuard { LockoutResult = true };
        var actorId = Guid.NewGuid();
        var handler = new DeleteGroupCommandHandler(db, spy);

        var result = await handler.Handle(
            new DeleteGroupCommand(group.Id, actorId, ActorIsRealmSuperUser: true),
            CancellationToken.None);

        // Handler ĐÃ GỌI guard đúng 1 lần với đúng tham số (actor, group, realm-superuser flag).
        Assert.Equal(1, spy.DeleteCalls);
        Assert.Equal((actorId, group.Id, true), spy.LastDeleteArgs);
        // Guard báo lockout → handler phải chặn và KHÔNG xóa.
        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);
        Assert.NotNull(await db.PermissionGroups.FindAsync(group.Id));
    }

    [Fact]
    public async Task DeleteGroupHandler_GuardSaysNoLockout_ProceedsToDelete()
    {
        await using var db = CreateContext(nameof(DeleteGroupHandler_GuardSaysNoLockout_ProceedsToDelete));
        var group = new PermissionGroup { Name = "WireDeletable" };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();

        var spy = new SpyLockoutGuard { LockoutResult = false };
        var handler = new DeleteGroupCommandHandler(db, spy);

        var result = await handler.Handle(
            new DeleteGroupCommand(group.Id, Guid.NewGuid(), ActorIsRealmSuperUser: false),
            CancellationToken.None);

        Assert.Equal(1, spy.DeleteCalls);
        Assert.True(result.Success); // guard cho qua → handler tiếp tục xóa
        Assert.Null(await db.PermissionGroups.FindAsync(group.Id));
    }

    [Fact]
    public async Task UpdateGroupPermissionsHandler_CallsGuard_WithDraftsAndSuperFlag()
    {
        await using var db = CreateContext(nameof(UpdateGroupPermissionsHandler_CallsGuard_WithDraftsAndSuperFlag));
        var group = new PermissionGroup { Name = "WirePerms" };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();

        var spy = new SpyLockoutGuard { LockoutResult = true };
        var actorId = Guid.NewGuid();
        var handler = new UpdateGroupPermissionsCommandHandler(db, spy);
        var perms = new List<GroupPermissionEntry>
        {
            new("assets.view", PermissionValue.Grant),
            new("assets.edit", PermissionValue.Deny)
        };

        var result = await handler.Handle(
            new UpdateGroupPermissionsCommand(group.Id, perms, actorId, ActorIsRealmSuperUser: false),
            CancellationToken.None);

        // Handler ĐÃ GỌI guard với drafts chuyển đổi đúng từ entries (2 draft, đúng super flag).
        Assert.Equal(1, spy.GroupPermissionEditCalls);
        Assert.Equal((actorId, group.Id, 2, false), spy.LastPermEditArgs);
        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);
        // Full-replace KHÔNG được thực hiện khi bị chặn.
        Assert.Empty(await db.GroupPermissions.Where(gp => gp.GroupId == group.Id).ToListAsync());
    }

    [Fact]
    public async Task UpdateGroupPermissionsHandler_FullReplace_And_LogMeta_OldNew()
    {
        await using var db = CreateContext(nameof(UpdateGroupPermissionsHandler_FullReplace_And_LogMeta_OldNew));
        var group = new PermissionGroup { Name = "PermsReplace" };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.GroupPermissions.AddRange(
            new GroupPermission { GroupId = group.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant },
            new GroupPermission { GroupId = group.Id, PermissionKey = "assets.delete", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        // Guard cho qua (spy false) → full-replace thực hiện: 2 cũ bị thay bằng 1 mới.
        var spy = new SpyLockoutGuard { LockoutResult = false };
        var actorId = Guid.NewGuid();
        var handler = new UpdateGroupPermissionsCommandHandler(db, spy);
        var result = await handler.Handle(
            new UpdateGroupPermissionsCommand(
                group.Id,
                new List<GroupPermissionEntry> { new("assets.view", PermissionValue.Deny) },
                actorId,
                ActorIsRealmSuperUser: false),
            CancellationToken.None);

        Assert.True(result.Success);
        var remaining = await db.GroupPermissions.Where(gp => gp.GroupId == group.Id).ToListAsync();
        Assert.Single(remaining); // full-replace: cũ bị xóa sạch, chỉ còn bộ mới
        Assert.Equal("assets.view", remaining[0].PermissionKey);
        Assert.Equal(PermissionValue.Deny, remaining[0].Value);
        // LogMeta: old[] (int values) vs new[] — verbatim pre-migration serialize.
        Assert.Contains("\"old\"", result.LogMeta);
        Assert.Contains("\"new\"", result.LogMeta);
        Assert.Contains("assets.delete", result.LogMeta); // old set present in LogMeta
        Assert.Contains("Group permissions updated: PermsReplace", result.Note);
    }
}
