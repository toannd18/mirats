using aspire_react.Server.Application.Common.CompanyScope;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [DF-8.2] Shared company-assignment rules used by all Create/Update handlers.
/// Entity-specific handler suites exercise the same service; this matrix pins the rules so a new
/// handler cannot silently reintroduce body-controlled company assignment.
/// </summary>
public class CompanyAssignmentPolicyTests
{
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();

    private static TestHelpers.FakeScope Scoped() => new() { Super = false, CompanyId = CompanyA };
    private static TestHelpers.SuperUserScope Super() => new();

    [Theory]
    [InlineData("Asset")]
    [InlineData("Accessory")]
    [InlineData("Consumable")]
    [InlineData("Department")]
    [InlineData("Location")]
    [InlineData("SystemInfo")]
    [InlineData("User")]
    [InlineData("License")]
    public async Task Create_NonSuperUser_Null_FillsOwnCompany(string entity)
    {
        _ = entity;
        var result = await CompanyAssignmentPolicy.ForCreateAsync(Scoped(), null);
        Assert.True(result.Allowed);
        Assert.Equal(CompanyA, result.CompanyId);
    }

    [Theory]
    [InlineData("Asset")]
    [InlineData("Accessory")]
    [InlineData("Consumable")]
    [InlineData("Department")]
    [InlineData("Location")]
    [InlineData("SystemInfo")]
    [InlineData("User")]
    [InlineData("License")]
    public async Task Create_NonSuperUser_OtherCompany_Forbidden(string entity)
    {
        _ = entity;
        var result = await CompanyAssignmentPolicy.ForCreateAsync(Scoped(), CompanyB);
        Assert.False(result.Allowed);
    }

    [Theory]
    [InlineData("Asset")]
    [InlineData("Accessory")]
    [InlineData("Consumable")]
    [InlineData("Department")]
    [InlineData("Location")]
    [InlineData("SystemInfo")]
    [InlineData("User")]
    [InlineData("License")]
    public async Task Update_NonSuperUser_ChangeOrClearCompany_Forbidden(string entity)
    {
        _ = entity;
        Assert.False(await CompanyAssignmentPolicy.CanChangeAsync(Scoped(), CompanyB));
        Assert.False(await CompanyAssignmentPolicy.CanChangeAsync(Scoped(), Guid.Empty));
    }

    [Fact]
    public async Task Update_NonSuperUser_SameCompany_Allowed()
        => Assert.True(await CompanyAssignmentPolicy.CanChangeAsync(Scoped(), CompanyA));

    [Fact]
    public async Task Update_AbsentCompany_IsNotAChange()
        => Assert.True(await CompanyAssignmentPolicy.CanChangeAsync(Scoped(), null));

    [Fact]
    public async Task Create_SuperUser_Null_RemainsFloater()
    {
        var result = await CompanyAssignmentPolicy.ForCreateAsync(Super(), null);
        Assert.True(result.Allowed);
        Assert.Null(result.CompanyId);
    }

    [Fact]
    public async Task Update_SuperUser_MayClearOrMove()
    {
        Assert.True(await CompanyAssignmentPolicy.CanChangeAsync(Super(), Guid.Empty));
        Assert.True(await CompanyAssignmentPolicy.CanChangeAsync(Super(), CompanyB));
    }

    [Fact]
    public async Task Create_CompanylessNonSuperUser_KeepsLegacyValue()
    {
        var scope = new TestHelpers.FakeScope { Super = false, CompanyId = null };
        var result = await CompanyAssignmentPolicy.ForCreateAsync(scope, null);
        Assert.True(result.Allowed);
        Assert.Null(result.CompanyId);
    }
}
