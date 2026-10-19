using System.Text.Json;
using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Domain.Enums;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B2] Hợp đồng của helper dựng `LogMeta.changes`:
/// snake_case, chỉ field thực sự đổi, null khi không đổi, trường nhạy cảm không lộ giá trị,
/// Set trước Track phải ném (chống lớp lỗi BUG-P).
/// </summary>
public class ActionLogChangesTests
{
    [Fact]
    public void ToLogMeta_ChangedField_EmitsOldAndNew_WithSnakeCaseName()
    {
        var meta = new ActionLogChanges()
            .Track("purchaseCost", 10m)
            .Set("purchaseCost", 12.5m)
            .ToLogMeta();

        Assert.Equal("""{"changes":{"purchase_cost":{"old":10,"new":12.5}}}""", meta);
    }

    [Fact]
    public void ToLogMeta_AlreadySnakeCase_IsKeptAsIs()
    {
        var meta = new ActionLogChanges()
            .Track("purchase_cost", 1m)
            .Set("purchase_cost", 2m)
            .ToLogMeta();

        Assert.Contains("\"purchase_cost\"", meta);
        Assert.DoesNotContain("purchase_ccost", meta);
    }

    [Fact]
    public void ToLogMeta_NoFieldChanged_ReturnsNull()
    {
        var meta = new ActionLogChanges()
            .Track("name", "Cùng tên")
            .Track("minAmt", 2)
            .Set("name", "Cùng tên")
            .Set("minAmt", 2)
            .ToLogMeta();

        Assert.Null(meta);
    }

    [Fact]
    public void ToLogMeta_OnlyActuallyChangedFields_AreIncluded()
    {
        var meta = new ActionLogChanges()
            .Track("name", "Cũ")
            .Track("notes", "ghi chú")
            .Track("minAmt", 3)
            .Set("name", "Mới")
            .Set("notes", "ghi chú")
            .Set("minAmt", 3)
            .ToLogMeta();

        using var doc = JsonDocument.Parse(meta!);
        var changes = doc.RootElement.GetProperty("changes");
        Assert.Equal(new[] { "name" }, changes.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Cũ", changes.GetProperty("name").GetProperty("old").GetString());
        Assert.Equal("Mới", changes.GetProperty("name").GetProperty("new").GetString());
    }

    [Fact]
    public void ToLogMeta_FieldTrackedButNotSet_IsIgnored()
    {
        var meta = new ActionLogChanges()
            .Track("name", "Cũ")
            .Track("never_set", 1)
            .Set("name", "Mới")
            .ToLogMeta();

        Assert.NotNull(meta);
        Assert.DoesNotContain("never_set", meta);
    }

    [Fact]
    public void ToLogMeta_SensitiveField_OnlyReportsChanged_AndNeverLeaksValues()
    {
        var meta = new ActionLogChanges()
            .TrackSensitive("serial", "SN-OLD-SECRET")
            .SetSensitive("serial", "SN-NEW-SECRET")
            .ToLogMeta();

        Assert.Equal("""{"changes":{"serial":{"changed":true}}}""", meta);
        Assert.DoesNotContain("SN-OLD-SECRET", meta);
        Assert.DoesNotContain("SN-NEW-SECRET", meta);
        Assert.DoesNotContain("old", meta);
        Assert.DoesNotContain("new", meta);
    }

    [Fact]
    public void ToLogMeta_SensitiveFieldUnchanged_IsOmitted()
    {
        var meta = new ActionLogChanges()
            .TrackSensitive("serial", "SN-1")
            .SetSensitive("serial", "SN-1")
            .ToLogMeta();

        Assert.Null(meta);
    }

    [Fact]
    public void Set_WithoutTrack_Throws_GuardingBugP()
    {
        var changes = new ActionLogChanges();

        var ex = Assert.Throws<InvalidOperationException>(() => changes.Set("cost", 10m));
        Assert.Contains("Track()", ex.Message);
    }

    [Fact]
    public void ToLogMeta_SetOfGuids_IsOrderInsensitive()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var meta = new ActionLogChanges()
            .Track("assignees", new List<Guid> { a, b })
            .Set("assignees", new[] { b, a })
            .ToLogMeta();

        Assert.Null(meta);
    }

    [Fact]
    public void ToLogMeta_SetOfGuids_ChangedMembership_ReportsOldAndNew()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var meta = new ActionLogChanges()
            .Track("assignees", new List<Guid> { a, b })
            .Set("assignees", new[] { a, c })
            .ToLogMeta();

        using var doc = JsonDocument.Parse(meta!);
        var entry = doc.RootElement.GetProperty("changes").GetProperty("assignees");
        Assert.Equal(2, entry.GetProperty("old").GetArrayLength());
        Assert.Equal(2, entry.GetProperty("new").GetArrayLength());
        var newIds = entry.GetProperty("new").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        Assert.Contains(c, newIds);
        Assert.DoesNotContain(b, newIds);
    }

    [Fact]
    public void ToLogMeta_Enums_SerializeAsStrings()
    {
        var meta = new ActionLogChanges()
            .Track("type", AssetMaintenanceType.Repair)
            .Set("type", AssetMaintenanceType.Maintenance)
            .ToLogMeta();

        using var doc = JsonDocument.Parse(meta!);
        var entry = doc.RootElement.GetProperty("changes").GetProperty("type");
        Assert.Equal("Repair", entry.GetProperty("old").GetString());
        Assert.Equal("Maintenance", entry.GetProperty("new").GetString());
    }

    [Fact]
    public void ToLogMeta_NullToValue_IsAChange()
    {
        var meta = new ActionLogChanges()
            .Track("supplierId", null)
            .Set("supplierId", Guid.Empty)
            .ToLogMeta();

        using var doc = JsonDocument.Parse(meta!);
        var entry = doc.RootElement.GetProperty("changes").GetProperty("supplier_id");
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("old").ValueKind);
        Assert.Equal(Guid.Empty, entry.GetProperty("new").GetGuid());
    }

    [Fact]
    public void ToLogMeta_NullableAndPlainOfSameValue_AreEqual()
    {
        int? oldValue = 5;
        var meta = new ActionLogChanges()
            .Track("minAmt", oldValue)
            .Set("minAmt", 5)
            .ToLogMeta();

        Assert.Null(meta);
    }

    /// <summary>[AUDIT-LOG B6a] `ToChangesMap` — dạng MAP để command GHÉP thêm key phụ cạnh `changes`.</summary>
    [Fact]
    public void ToChangesMap_ReturnsOnlyChangedFields_AndIsEmptyWhenNothingChanged()
    {
        var changed = new ActionLogChanges()
            .Track("a", 1)
            .Track("b", 2)
            .Set("a", 3)
            .Set("b", 2)
            .ToChangesMap();

        Assert.Single(changed);
        Assert.True(changed.ContainsKey("a"));
        Assert.False(changed.ContainsKey("b"));

        var unchanged = new ActionLogChanges()
            .Track("x", 1)
            .Set("x", 1)
            .ToChangesMap();
        Assert.Empty(unchanged);
    }

    [Fact]
    public void ToSnakeCase_HandlesCamelPascalAndAcronyms()
    {
        Assert.Equal("purchase_cost", ActionLogChanges.ToSnakeCase("purchaseCost"));
        Assert.Equal("item_no", ActionLogChanges.ToSnakeCase("ItemNo"));
        Assert.Equal("purchase_cost", ActionLogChanges.ToSnakeCase("purchase_cost"));
        Assert.Equal("url", ActionLogChanges.ToSnakeCase("URL"));
        Assert.Equal("is_super_user", ActionLogChanges.ToSnakeCase("isSuperUser"));
    }
}
