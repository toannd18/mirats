using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

public class LicenseTests
{
    private sealed class SuperUserScope : ICompanyScopeService
    {
        public bool IsSuperUser() => true;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(null);
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId) => Task.FromResult(true);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid GetLocalUserId() => UserId;
    }

    private sealed class FakeScope : ICompanyScopeService
    {
        public bool Super { get; set; }
        public Guid? CompanyId { get; set; }
        public bool IsSuperUser() => Super;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult(Super ? (Guid?)null : CompanyId);
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId)
            => Task.FromResult(Super || CompanyId == null || CompanyId == companyId);
    }

    private static AppDbContext CreateContext(string name)
    {
        // Suppress the InMemory transaction warning so BeginTransactionAsync (used by the Task O-FIX
        // seat-picking lock path) becomes a no-op under InMemory — same as TestHelpers.CreateContext.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new SuperUserScope());
    }

    private static LicensesController CreateController(AppDbContext ctx, bool super, Guid? companyId = null)
        => new(TestHelpers.BuildMediator(ctx, new FakeScope { Super = super, CompanyId = companyId }));

    private static string ReadErrorCode(object? value)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        return doc.RootElement.GetProperty("error_code").GetString()!;
    }

    private static async Task<(Guid licenseId, Guid categoryId, Guid companyId)> SeedLicenseAsync(
        AppDbContext ctx, int seats = 3, Guid? companyId = null, bool reassignable = true)
    {
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var category = new Category { Name = "Software", CategoryType = CategoryType.License };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var license = new License
        {
            Name = "Windows Pro",
            Seats = seats,
            Reassignable = reassignable,
            CategoryId = category.Id,
            CompanyId = companyId ?? company.Id
        };
        ctx.Licenses.Add(license);
        for (var i = 1; i <= seats; i++)
            ctx.LicenseSeats.Add(new LicenseSeat { LicenseId = license.Id, SeatNumber = i });
        await ctx.SaveChangesAsync();
        return (license.Id, category.Id, company.Id);
    }

    private static async Task<(Guid userId, Guid assetId)> SeedTargetsAsync(AppDbContext ctx, Guid companyId)
    {
        var user = new User { Username = "u1", FirstName = "A", LastName = "B", CompanyId = companyId };
        var asset = new Asset { AssetTag = "AST-001", Name = "Laptop", IsConfirmed = true, CompanyId = companyId };
        ctx.Users.Add(user);
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        return (user.Id, asset.Id);
    }

    private static async Task<Guid> SeedSystemInfoAsync(AppDbContext ctx, Guid companyId)
    {
        var sysInfo = new SystemInfo { Name = "He thong A", CompanyId = companyId };
        ctx.SystemInfos.Add(sysInfo);
        await ctx.SaveChangesAsync();
        return sysInfo.Id;
    }
    [Fact]
    public async Task Create_AutoGeneratesSeats_MatchingSeatCount()
    {
        await using var ctx = CreateContext(nameof(Create_AutoGeneratesSeats_MatchingSeatCount));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var category = new Category { Name = "Software", CategoryType = CategoryType.License };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx, super: true);
        var result = await controller.Create(new CreateLicenseRequest("Windows Pro", null, 5,
            Reassignable: true, CategoryId: category.Id, CompanyId: company.Id));
        Assert.IsType<OkObjectResult>(result);

        var license = await ctx.Licenses.SingleAsync();
        Assert.Equal(5, license.Seats);
        Assert.Equal(5, await ctx.LicenseSeats.CountAsync(s => s.LicenseId == license.Id));
        var numbers = await ctx.LicenseSeats.Where(s => s.LicenseId == license.Id).Select(s => s.SeatNumber).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, numbers.OrderBy(n => n));
    }

    [Fact]
    public async Task Create_RegularUser_ForcesOwnCompany()
    {
        await using var ctx = CreateContext(nameof(Create_RegularUser_ForcesOwnCompany));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var category = new Category { Name = "Software", CategoryType = CategoryType.License };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx, super: false, companyId: company.Id);
        var result = await controller.Create(new CreateLicenseRequest("Win", null, 2, CategoryId: category.Id));
        Assert.IsType<OkObjectResult>(result);
        var license = await ctx.Licenses.SingleAsync();
        Assert.Equal(company.Id, license.CompanyId);
    }

    [Fact]
    public async Task Update_IncreaseSeats_AddsNewSeats()
    {
        await using var ctx = CreateContext(nameof(Update_IncreaseSeats_AddsNewSeats));
        var (licenseId, _, _) = await SeedLicenseAsync(ctx, seats: 2);
        var controller = CreateController(ctx, super: true);

        var result = await controller.Update(licenseId, new UpdateLicenseRequest(Seats: 5));
        Assert.IsType<OkObjectResult>(result);
        var license = await ctx.Licenses.SingleAsync(x => x.Id == licenseId);
        Assert.Equal(5, license.Seats);
        var numbers = await ctx.LicenseSeats.Where(s => s.LicenseId == licenseId).Select(s => s.SeatNumber).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, numbers.OrderBy(n => n));
    }

    [Fact]
    public async Task Update_DecreaseSeats_NotEnoughFree_ReturnsError()
    {
        await using var ctx = CreateContext(nameof(Update_DecreaseSeats_NotEnoughFree_ReturnsError));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 3);
        var (userId, _) = await SeedTargetsAsync(ctx, companyId);
        var controller = CreateController(ctx, super: true);

        var seats = await ctx.LicenseSeats.Where(s => s.LicenseId == licenseId).ToListAsync();
        foreach (var s in seats) { s.UserId = userId; s.AssignedAt = DateTime.UtcNow; }
        await ctx.SaveChangesAsync();

        var result = await controller.Update(licenseId, new UpdateLicenseRequest(Seats: 2));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CANNOT_REDUCE_SEATS_IN_USE", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task Update_DecreaseSeats_WithEnoughFree_Succeeds()
    {
        await using var ctx = CreateContext(nameof(Update_DecreaseSeats_WithEnoughFree_Succeeds));
        var (licenseId, _, _) = await SeedLicenseAsync(ctx, seats: 3);
        var controller = CreateController(ctx, super: true);

        var result = await controller.Update(licenseId, new UpdateLicenseRequest(Seats: 2));
        Assert.IsType<OkObjectResult>(result);
        var license = await ctx.Licenses.SingleAsync(x => x.Id == licenseId);
        Assert.Equal(2, license.Seats);
        Assert.Equal(2, await ctx.LicenseSeats.CountAsync(s => s.LicenseId == licenseId));
    }

    [Fact]
    public async Task Update_CannotChangeCompany_Locked()
    {
        await using var ctx = CreateContext(nameof(Update_CannotChangeCompany_Locked));
        var (licenseId, _, _) = await SeedLicenseAsync(ctx);
        var otherCompany = new Company { Name = "CT-B" };
        ctx.Companies.Add(otherCompany);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, super: true);

        var result = await controller.Update(licenseId, new UpdateLicenseRequest(CompanyId: otherCompany.Id));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("FIELD_LOCKED", ReadErrorCode(bad.Value));
    }
    // ==================== Checkout ====================

    [Fact]
    public async Task Checkout_ForUser_Succeeds()
    {
        await using var ctx = CreateContext(nameof(Checkout_ForUser_Succeeds));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 2);
        var (userId, _) = await SeedTargetsAsync(ctx, companyId);
        var controller = CreateController(ctx, super: true);

        var result = await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.User, userId));
        Assert.IsType<OkObjectResult>(result);
        var seat = await ctx.LicenseSeats.FirstAsync(s => s.LicenseId == licenseId && s.SeatNumber == 1);
        Assert.Equal(userId, seat.UserId);
        Assert.Null(seat.AssetId);
        Assert.Null(seat.SystemInfoId);
        Assert.NotNull(seat.AssignedAt);
    }

    [Fact]
    public async Task Checkout_ForAsset_Succeeds()
    {
        await using var ctx = CreateContext(nameof(Checkout_ForAsset_Succeeds));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1);
        var (_, assetId) = await SeedTargetsAsync(ctx, companyId);
        var controller = CreateController(ctx, super: true);

        var result = await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.Asset, assetId));
        Assert.IsType<OkObjectResult>(result);
        var seat = await ctx.LicenseSeats.SingleAsync();
        Assert.Equal(assetId, seat.AssetId);
        Assert.Null(seat.UserId);
    }

    [Fact]
    public async Task Checkout_ForSystemInfo_Succeeds()
    {
        await using var ctx = CreateContext(nameof(Checkout_ForSystemInfo_Succeeds));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1);
        var sysId = await SeedSystemInfoAsync(ctx, companyId);
        var controller = CreateController(ctx, super: true);

        var result = await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.SystemInfo, sysId));
        Assert.IsType<OkObjectResult>(result);
        var seat = await ctx.LicenseSeats.SingleAsync();
        Assert.Equal(sysId, seat.SystemInfoId);
        Assert.Null(seat.UserId);
        Assert.Null(seat.AssetId);
    }

    [Fact]
    public async Task Checkout_MissingTarget_ReturnsRequired()
    {
        await using var ctx = CreateContext(nameof(Checkout_MissingTarget_ReturnsRequired));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1);
        await SeedTargetsAsync(ctx, companyId);
        var controller = CreateController(ctx, super: true);

        var result = await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.User, null));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("TARGET_REQUIRED", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task Checkout_LegacyAssign_BothTargets_ReturnsAmbiguous()
    {
        await using var ctx = CreateContext(nameof(Checkout_LegacyAssign_BothTargets_ReturnsAmbiguous));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1);
        var (userId, assetId) = await SeedTargetsAsync(ctx, companyId);
        var seatId = await ctx.LicenseSeats.Where(s => s.LicenseId == licenseId).Select(s => s.Id).SingleAsync();
        var controller = CreateController(ctx, super: true);

        var result = await controller.AssignSeatLegacy(licenseId, new AssignSeatRequest(seatId, assetId, userId));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("SEAT_TARGET_AMBIGUOUS", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task Checkout_AlreadyAssignedSeat_ReturnsError()
    {
        await using var ctx = CreateContext(nameof(Checkout_AlreadyAssignedSeat_ReturnsError));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1);
        var (userId, _) = await SeedTargetsAsync(ctx, companyId);
        var seatId = await ctx.LicenseSeats.Where(s => s.LicenseId == licenseId).Select(s => s.Id).SingleAsync();
        var controller = CreateController(ctx, super: true);

        await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(seatId, LicenseSeatTargetType.User, userId));
        var (_, assetId) = await SeedTargetsAsync(ctx, companyId);
        var result = await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(seatId, LicenseSeatTargetType.Asset, assetId));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("SEAT_ALREADY_ASSIGNED", ReadErrorCode(bad.Value));
    }
    // ==================== Checkin ====================

    [Fact]
    public async Task Checkin_NotReassignable_ReturnsError()
    {
        await using var ctx = CreateContext(nameof(Checkin_NotReassignable_ReturnsError));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1, reassignable: false);
        var (userId, _) = await SeedTargetsAsync(ctx, companyId);
        var seatId = await ctx.LicenseSeats.Where(s => s.LicenseId == licenseId).Select(s => s.Id).SingleAsync();
        var controller = CreateController(ctx, super: true);

        await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(seatId, LicenseSeatTargetType.User, userId));
        var result = await controller.CheckinSeat(licenseId, new CheckinLicenseSeatRequest(seatId));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("LICENSE_NOT_REASSIGNABLE", ReadErrorCode(bad.Value));
        var seat = await ctx.LicenseSeats.SingleAsync();
        Assert.Equal(userId, seat.UserId);
    }

    [Fact]
    public async Task Checkin_Reassignable_Succeeds_AndSeatIsFree()
    {
        await using var ctx = CreateContext(nameof(Checkin_Reassignable_Succeeds_AndSeatIsFree));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1, reassignable: true);
        var (userId, _) = await SeedTargetsAsync(ctx, companyId);
        var seatId = await ctx.LicenseSeats.Where(s => s.LicenseId == licenseId).Select(s => s.Id).SingleAsync();
        var controller = CreateController(ctx, super: true);

        await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(seatId, LicenseSeatTargetType.User, userId));
        var result = await controller.CheckinSeat(licenseId, new CheckinLicenseSeatRequest(seatId));
        Assert.IsType<OkObjectResult>(result);
        var seat = await ctx.LicenseSeats.SingleAsync();
        Assert.Null(seat.UserId);
        Assert.Null(seat.AssetId);
        Assert.Null(seat.SystemInfoId);
    }

    // ==================== Company-scoping ====================

    [Fact]
    public async Task Checkout_CrossCompanyUser_ReturnsCompanyMismatch()
    {
        await using var ctx = CreateContext(nameof(Checkout_CrossCompanyUser_ReturnsCompanyMismatch));
        var (licenseId, _, _) = await SeedLicenseAsync(ctx, seats: 1);
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.Add(companyB);
        await ctx.SaveChangesAsync();
        var userB = new User { Username = "b1", FirstName = "B", LastName = "B", CompanyId = companyB.Id };
        ctx.Users.Add(userB);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, super: true);

        var result = await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.User, userB.Id));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("LICENSE_COMPANY_MISMATCH", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task Checkout_SystemInfo_CrossCompany_ReturnsCompanyMismatch()
    {
        await using var ctx = CreateContext(nameof(Checkout_SystemInfo_CrossCompany_ReturnsCompanyMismatch));
        var (licenseId, _, _) = await SeedLicenseAsync(ctx, seats: 1);
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.Add(companyB);
        await ctx.SaveChangesAsync();
        var sysB = await SeedSystemInfoAsync(ctx, companyB.Id);
        var controller = CreateController(ctx, super: true);

        var result = await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.SystemInfo, sysB));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("LICENSE_COMPANY_MISMATCH", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task GetLicense_CrossCompany_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(GetLicense_CrossCompany_ReturnsNotFound));
        var (licenseId, _, _) = await SeedLicenseAsync(ctx, seats: 1);
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.Add(companyB);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx, super: false, companyId: companyB.Id);
        var result = await controller.GetLicense(licenseId);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ==================== Delete guard ====================

    [Fact]
    public async Task Delete_AfterCheckout_ReturnsLicenseInUse()
    {
        await using var ctx = CreateContext(nameof(Delete_AfterCheckout_ReturnsLicenseInUse));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 1);
        var (userId, _) = await SeedTargetsAsync(ctx, companyId);
        var controller = CreateController(ctx, super: true);

        await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.User, userId));
        var result = await controller.Delete(licenseId);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("LICENSE_IN_USE", ReadErrorCode(bad.Value));
        Assert.NotNull(await ctx.Licenses.FirstOrDefaultAsync(x => x.Id == licenseId && x.DeletedAt == null));
    }

    [Fact]
    public async Task Delete_NeverCheckedOut_Succeeds()
    {
        await using var ctx = CreateContext(nameof(Delete_NeverCheckedOut_Succeeds));
        var (licenseId, _, _) = await SeedLicenseAsync(ctx, seats: 2);
        var controller = CreateController(ctx, super: true);

        var result = await controller.Delete(licenseId);
        Assert.IsType<OkObjectResult>(result);
        var l = await ctx.Licenses.SingleAsync();
        Assert.NotNull(l.DeletedAt);
    }

    // ==================== Warning flags ====================

    [Fact]
    public async Task List_ExpiringSoon_And_LowSeats_Filter()
    {
        await using var ctx = CreateContext(nameof(List_ExpiringSoon_And_LowSeats_Filter));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var category = new Category { Name = "Software", CategoryType = CategoryType.License };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var expiring = new License { Name = "Sap het han", Seats = 2, MinSeats = 2, ExpirationDate = DateTime.UtcNow.AddDays(10), CategoryId = category.Id, CompanyId = company.Id };
        var expired = new License { Name = "Da het han", Seats = 2, ExpirationDate = DateTime.UtcNow.AddDays(-5), CategoryId = category.Id, CompanyId = company.Id };
        var ok = new License { Name = "Binh thuong", Seats = 10, ExpirationDate = DateTime.UtcNow.AddYears(1), CategoryId = category.Id, CompanyId = company.Id };
        ctx.Licenses.AddRange(expiring, expired, ok);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx, super: true);
        var result = await controller.GetLicenses(null, null, null, expiringSoon: true);
        var okr = Assert.IsType<OkObjectResult>(result);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(okr.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        var names = doc.RootElement.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Contains("Sap het han", names);
        Assert.Contains("Da het han", names);
        Assert.DoesNotContain("Binh thuong", names);

        // lowSeats: only the license with available (2) <= MinSeats (2) survives.
        var lowRes = await controller.GetLicenses(null, null, null, lowSeats: true);
        var lowOk = Assert.IsType<OkObjectResult>(lowRes);
        using var lowDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(lowOk.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        var lowNames = lowDoc.RootElement.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Contains("Sap het han", lowNames);
        Assert.DoesNotContain("Da het han", lowNames);
        Assert.DoesNotContain("Binh thuong", lowNames);
    }

    // ==================== Seats endpoint ====================

    [Fact]
    public async Task GetSeats_ReturnsSeatNumberAndTarget()
    {
        await using var ctx = CreateContext(nameof(GetSeats_ReturnsSeatNumberAndTarget));
        var (licenseId, _, companyId) = await SeedLicenseAsync(ctx, seats: 2);
        var (userId, _) = await SeedTargetsAsync(ctx, companyId);
        var controller = CreateController(ctx, super: true);
        await controller.CheckoutSeat(licenseId, new CheckoutLicenseSeatRequest(null, LicenseSeatTargetType.User, userId));

        var result = await controller.GetSeats(licenseId);
        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        var seats = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, seats.Count);
        Assert.Equal(1, seats[0].GetProperty("seatNumber").GetInt32());
        Assert.True(seats[0].GetProperty("assigned").GetBoolean());
        Assert.Equal("User", seats[0].GetProperty("targetType").GetString());
        Assert.False(seats[1].GetProperty("assigned").GetBoolean());
    }
}