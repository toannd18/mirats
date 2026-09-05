using System.Security.Claims;
using aspire_react.Server.Application.ImportExport;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task IMPORT-T5/T6 — Import company-scoping: the server re-validates a client-supplied target
/// company against the acting user's REAL scope (never trusts the client). Mirrors the 7 scenarios
/// verified against the real API in T5:
///   1. child-company user targets the parent company        → out of scope (403)
///   2. parent-company user targets a child company          → in scope (200)
///   3. child-company user targets their own company         → in scope (200)
///   5. child-company user targets an unrelated branch       → out of scope (403)
///   6. superuser omits companyId                            → 400 COMPANY_REQUIRED
///   7. superuser/normal user targets a NON-EXISTENT company → out of scope (403)
///   8. superuser targets any real company                   → in scope (200)
/// Covers BOTH the scope-decision (ICompanyScopeService.IsCompanyIdInUserScopeAsync) and the
/// controller mapping (400/403/200 + the exact validated companyId forwarded to the import service).
/// </summary>
public class ImportCompanyScopeTests
{
    // ─────────────────────────── scope-decision tests ───────────────────────────

    private static async Task<(AppDbContext ctx, Guid parent, Guid child, Guid other, Guid parentUser, Guid childUser)> SeedCompaniesAsync(string dbName)
    {
        var ctx = TestHelpers.CreateContext(dbName);
        var parentCo = new Company { Name = "PARENT" };
        ctx.Companies.Add(parentCo);
        await ctx.SaveChangesAsync();

        var childCo = new Company { Name = "CHILD", ParentId = parentCo.Id };
        var otherCo = new Company { Name = "OTHER" };
        ctx.Companies.Add(childCo);
        ctx.Companies.Add(otherCo);

        var parentUser = new User { Username = "parent", Email = "p@t.local", FirstName = "P", LastName = "P", CompanyId = parentCo.Id };
        var childUser = new User { Username = "child", Email = "c@t.local", FirstName = "C", LastName = "C", CompanyId = childCo.Id };
        ctx.Users.Add(parentUser);
        ctx.Users.Add(childUser);
        await ctx.SaveChangesAsync();
        return (ctx, parentCo.Id, childCo.Id, otherCo.Id, parentUser.Id, childUser.Id);
    }

    private static DefaultHttpContext BuildHttpContext(ClaimsPrincipal principal, AppDbContext ctx)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var services = new ServiceCollection();
        services.AddSingleton(ctx); // resolve the SAME seeded AppDbContext from RequestServices
        httpContext.RequestServices = services.BuildServiceProvider();
        return httpContext;
    }

    private static ClaimsPrincipal RegularUser(Guid localUserId) =>
        new(new ClaimsIdentity(new[] { new Claim("local_user_id", localUserId.ToString()) }, "Test"));

    private static ClaimsPrincipal SuperUser() =>
        new(new ClaimsIdentity(new[] { new Claim("realm_access", """{"roles":["admin"]}""") }, "Test"));

    private static ClaimsPrincipal MissingClaim() =>
        new(new ClaimsIdentity(new[] { new Claim("preferred_username", "child") }, "Test"));

    private static CompanyScopeService CreateService(DefaultHttpContext httpContext)
        => new(new HttpContextAccessor { HttpContext = httpContext }, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task ChildUser_TargetingParentCompany_OutOfScope()
    {
        var s = await SeedCompaniesAsync(nameof(ChildUser_TargetingParentCompany_OutOfScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(RegularUser(s.childUser), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.parent)); // 403 scenario
    }

    [Fact]
    public async Task ChildUser_TargetingOwnCompany_InScope()
    {
        var s = await SeedCompaniesAsync(nameof(ChildUser_TargetingOwnCompany_InScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(RegularUser(s.childUser), ctx));

        Assert.True(await svc.IsCompanyIdInUserScopeAsync(s.child)); // 200 scenario
    }

    [Fact]
    public async Task ChildUser_TargetingUnrelatedBranch_OutOfScope()
    {
        var s = await SeedCompaniesAsync(nameof(ChildUser_TargetingUnrelatedBranch_OutOfScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(RegularUser(s.childUser), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.other)); // 403 scenario (other branch)
    }

    [Fact]
    public async Task ParentUser_TargetingOwnCompany_InScope()
    {
        var s = await SeedCompaniesAsync(nameof(ParentUser_TargetingOwnCompany_InScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(RegularUser(s.parentUser), ctx));

        Assert.True(await svc.IsCompanyIdInUserScopeAsync(s.parent));
    }

    [Fact]
    public async Task ParentUser_TargetingChildCompany_InScope()
    {
        var s = await SeedCompaniesAsync(nameof(ParentUser_TargetingChildCompany_InScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(RegularUser(s.parentUser), ctx));

        Assert.True(await svc.IsCompanyIdInUserScopeAsync(s.child)); // 200 scenario (parent → child)
    }

    [Fact]
    public async Task ParentUser_TargetingUnrelatedBranch_OutOfScope()
    {
        var s = await SeedCompaniesAsync(nameof(ParentUser_TargetingUnrelatedBranch_OutOfScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(RegularUser(s.parentUser), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.other));
    }

    [Fact]
    public async Task SuperUser_TargetingAnyRealCompany_InScope()
    {
        var s = await SeedCompaniesAsync(nameof(SuperUser_TargetingAnyRealCompany_InScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(SuperUser(), ctx));

        Assert.True(await svc.IsCompanyIdInUserScopeAsync(s.parent));
        Assert.True(await svc.IsCompanyIdInUserScopeAsync(s.child));
        Assert.True(await svc.IsCompanyIdInUserScopeAsync(s.other)); // 200 scenario (any company)
    }

    [Fact]
    public async Task SuperUser_TargetingNonexistentCompany_OutOfScope()
    {
        var s = await SeedCompaniesAsync(nameof(SuperUser_TargetingNonexistentCompany_OutOfScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(SuperUser(), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(Guid.NewGuid())); // 403 scenario (nonexistent)
    }

    [Fact]
    public async Task RegularUser_TargetingNonexistentCompany_OutOfScope()
    {
        var s = await SeedCompaniesAsync(nameof(RegularUser_TargetingNonexistentCompany_OutOfScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(RegularUser(s.childUser), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CompanyLessUser_TargetingAnyCompany_OutOfScope()
    {
        // [SEC-FIX JIT-COMPANYLESS, 2026-08-23] A regular user WITHOUT a company (JIT-created on
        // first login, admin has not assigned one yet) may NOT import into ANY specific company —
        // previously IsCompanyIdInUserScopeAsync returned true for every company when the user had
        // no CompanyId (task-verified: a company-less user with Admin permissions could read/import
        // every company's data). Superuser (realm role) still passes; this test pins the deny.
        var s = await SeedCompaniesAsync(nameof(CompanyLessUser_TargetingAnyCompany_OutOfScope));
        await using var ctx = s.ctx;
        var companyLessUser = new User { Username = "noco", Email = "noco@t.local", FirstName = "N", LastName = "C", CompanyId = null };
        ctx.Users.Add(companyLessUser);
        await ctx.SaveChangesAsync();
        var svc = CreateService(BuildHttpContext(RegularUser(companyLessUser.Id), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.parent));
        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.child));
        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.other));
    }

    [Fact]
    public async Task Unauthenticated_TargetingAnyCompany_OutOfScope()
    {
        var s = await SeedCompaniesAsync(nameof(Unauthenticated_TargetingAnyCompany_OutOfScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(new ClaimsPrincipal(), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.child));
    }

    [Fact]
    public async Task MissingLocalUserIdClaim_OutOfScope()
    {
        var s = await SeedCompaniesAsync(nameof(MissingLocalUserIdClaim_OutOfScope));
        await using var ctx = s.ctx;
        var svc = CreateService(BuildHttpContext(MissingClaim(), ctx));

        Assert.False(await svc.IsCompanyIdInUserScopeAsync(s.child)); // fail closed — no local id
    }

    // ─────────────────────────── controller mapping tests ───────────────────────────

    private sealed class RecordingImportService : IExcelImportService
    {
        public Guid? LastCompanyId { get; private set; }
        private static readonly ImportRowResult OkRow = new(1, true, "Đã import.");
        public Task<ImportSheetResult> ImportReferenceAsync(Stream s, Guid u, Guid c, CancellationToken ct = default)
        { LastCompanyId = c; return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
        public Task<ImportSheetResult> ImportAssetModelsAsync(Stream s, Guid u, Guid c, CancellationToken ct = default)
        { LastCompanyId = c; return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
        public Task<ImportSheetResult> ImportAssetsAsync(Stream s, Guid u, Guid c, CancellationToken ct = default)
        { LastCompanyId = c; return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
        public Task<ImportSheetResult> ImportComponentsAsync(Stream s, Guid u, Guid c, CancellationToken ct = default)
        { LastCompanyId = c; return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
        public Task<ImportSheetResult> ImportAccessoriesAsync(Stream s, Guid u, Guid c, CancellationToken ct = default)
        { LastCompanyId = c; return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
        public Task<ImportSheetResult> ImportConsumablesAsync(Stream s, Guid u, Guid c, CancellationToken ct = default)
        { LastCompanyId = c; return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
        public Task<ImportSheetResult> ImportSystemsAsync(Stream s, Guid u, Guid c, CancellationToken ct = default)
        { LastCompanyId = c; return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
        public Task<ImportSheetResult> ImportSystemPositionsAsync(Stream s, Guid u, Guid? c, CancellationToken ct = default)
        { return Task.FromResult(new ImportSheetResult(1, 0, new[] { OkRow }, Array.Empty<ImportRowResult>())); }
    }

    private static ImportExportController BuildController(
        AppDbContext ctx, ClaimsPrincipal principal, RecordingImportService excel)
    {
        var httpContext = BuildHttpContext(principal, ctx);
        var scope = new CompanyScopeService(new HttpContextAccessor { HttpContext = httpContext }, new MemoryCache(new MemoryCacheOptions()));
        var actionLog = new ActionLogService(ctx, new HttpContextAccessor { HttpContext = httpContext });
        var controller = new ImportExportController(
            TestHelpers.BuildMediator(ctx, scope, excelImport: excel),
            ctx, scope, actionLog);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static IFormFile FakeXlsx() => new FakeFormFile("test.xlsx", new byte[] { 0x50, 0x4B, 1, 2 });

    [Fact]
    public async Task MissingCompanyId_Returns400CompanyRequired()
    {
        var s = await SeedCompaniesAsync(nameof(MissingCompanyId_Returns400CompanyRequired));
        await using var ctx = s.ctx;
        var excel = new RecordingImportService();
        var c = BuildController(ctx, RegularUser(s.childUser), excel);

        var result = await c.ImportAssets(FakeXlsx(), Guid.Empty);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
        Assert.Null(excel.LastCompanyId); // nothing forwarded
    }

    [Fact]
    public async Task ChildUser_TargetingParentCompany_Returns403()
    {
        var s = await SeedCompaniesAsync(nameof(ChildUser_TargetingParentCompany_Returns403));
        await using var ctx = s.ctx;
        var excel = new RecordingImportService();
        var c = BuildController(ctx, RegularUser(s.childUser), excel);

        var result = await c.ImportAssets(FakeXlsx(), s.parent);

        Assert.IsType<ForbidResult>(result); // 403
        Assert.Null(excel.LastCompanyId); // rejected before forwarding
    }

    [Fact]
    public async Task ParentUser_TargetingChildCompany_Returns200AndForwardsValidatedCompanyId()
    {
        var s = await SeedCompaniesAsync(nameof(ParentUser_TargetingChildCompany_Returns200AndForwardsValidatedCompanyId));
        await using var ctx = s.ctx;
        var excel = new RecordingImportService();
        var c = BuildController(ctx, RegularUser(s.parentUser), excel);

        var result = await c.ImportAssets(FakeXlsx(), s.child);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(s.child, excel.LastCompanyId); // the validated companyId is forwarded (not the client-re-echoed one unchecked)
    }

    [Fact]
    public async Task ChildUser_TargetingOwnCompany_Returns200()
    {
        var s = await SeedCompaniesAsync(nameof(ChildUser_TargetingOwnCompany_Returns200));
        await using var ctx = s.ctx;
        var excel = new RecordingImportService();
        var c = BuildController(ctx, RegularUser(s.childUser), excel);

        var result = await c.ImportAssets(FakeXlsx(), s.child);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(s.child, excel.LastCompanyId);
    }

    [Fact]
    public async Task ChildUser_TargetingUnrelatedBranch_Returns403()
    {
        var s = await SeedCompaniesAsync(nameof(ChildUser_TargetingUnrelatedBranch_Returns403));
        await using var ctx = s.ctx;
        var excel = new RecordingImportService();
        var c = BuildController(ctx, RegularUser(s.childUser), excel);

        var result = await c.ImportAssets(FakeXlsx(), s.other);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(excel.LastCompanyId);
    }

    [Fact]
    public async Task SuperUser_TargetingNonexistent_Returns403()
    {
        var s = await SeedCompaniesAsync(nameof(SuperUser_TargetingNonexistent_Returns403));
        await using var ctx = s.ctx;
        var excel = new RecordingImportService();
        var c = BuildController(ctx, SuperUser(), excel);

        var result = await c.ImportAssets(FakeXlsx(), Guid.NewGuid());

        Assert.IsType<ForbidResult>(result);
        Assert.Null(excel.LastCompanyId);
    }

    [Fact]
    public async Task SuperUser_TargetingRealCompany_Returns200()
    {
        var s = await SeedCompaniesAsync(nameof(SuperUser_TargetingRealCompany_Returns200));
        await using var ctx = s.ctx;
        var excel = new RecordingImportService();
        var c = BuildController(ctx, SuperUser(), excel);

        var result = await c.ImportAssets(FakeXlsx(), s.other);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(s.other, excel.LastCompanyId);
    }

    /// <summary>Minimal IFormFile stub carrying a fake .xlsx payload.</summary>
    private sealed class FakeFormFile : IFormFile
    {
        private readonly byte[] _bytes;
        public FakeFormFile(string fileName, byte[] bytes) { FileName = fileName; _bytes = bytes; }
        public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public string ContentDisposition => $"form-data; name=file; filename={FileName}";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length => _bytes.Length;
        public string Name => "file";
        public string FileName { get; }
        public void CopyTo(Stream target) => target.Write(_bytes, 0, _bytes.Length);
        public Task CopyToAsync(Stream target, CancellationToken ct = default) => target.WriteAsync(_bytes, 0, _bytes.Length, ct);
        public Stream OpenReadStream() => new MemoryStream(_bytes);
    }
}
