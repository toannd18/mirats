using aspire_react.Server.Application.Depreciations.Commands;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

public class DepreciationCommandTests
{
    [Fact]
    public async Task Create_PersistsAndRejectsDuplicate()
    {
        await using var db = TestHelpers.CreateContext("depreciation-create-" + Guid.NewGuid());
        var handler = new CreateDepreciationCommandHandler(db);
        var first = await handler.Handle(
            new CreateDepreciationCommand("Khấu hao 36 tháng", 36, Guid.NewGuid()), default);
        Assert.True(first.Success);
        Assert.NotNull(first.Id);
        Assert.Equal(1, await db.Depreciations.CountAsync());

        var duplicate = await handler.Handle(
            new CreateDepreciationCommand("khấu hao 36 tháng", 24, Guid.NewGuid()), default);
        Assert.False(duplicate.Success);
        Assert.Equal("DUPLICATE_NAME", duplicate.ErrorCode);
        Assert.Equal(1, await db.Depreciations.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsInvalidMonths()
    {
        await using var db = TestHelpers.CreateContext("depreciation-invalid-" + Guid.NewGuid());
        var handler = new CreateDepreciationCommandHandler(db);
        var result = await handler.Handle(
            new CreateDepreciationCommand("Invalid", 0, Guid.NewGuid()), default);
        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
    }
}
