using System.Reflection;
using System.Security.Claims;
using aspire_react.Server.Application.Reports.RepairReports;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using aspire_react.Server.Web.Reports;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [REPAIR-REPORT] Endpoint-adjacent tests for the Excel export:
/// the workbook must consume the same <see cref="RepairReportResult"/> as the screen/print,
/// keep the three approved sheets, and expose both required authorization gates.
/// The real HTTP 403 is also verified against a temporary stack (the test cannot host MVC
/// without pulling the full authentication pipeline into the unit project).
/// </summary>
public class RepairReportExportTests
{
    [Fact]
    public void Workbook_HasThreeSheets_AndTotalsMatchTheResult()
    {
        var result = BuildResult();

        var bytes = RepairReportExcelBuilder.Build(result, new DateTime(2026, 10, 23, 9, 30, 0));

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(
            new[] { "Tổng hợp", "Theo kỳ và hệ thống", "Chi tiết phiếu" },
            wb.Worksheets.Select(ws => ws.Name).ToArray());

        var summary = wb.Worksheet("Tổng hợp");
        Assert.Equal(result.Summary.Failures, summary.Cell(7, 2).GetValue<int>());
        Assert.Equal(result.Summary.OpenCount, summary.Cell(8, 2).GetValue<int>());
        Assert.Equal(result.Summary.CompletedCount, summary.Cell(9, 2).GetValue<int>());
        Assert.Equal(2.3, summary.Cell(10, 2).GetDouble(), 1);
        Assert.Equal("0.0", summary.Cell(10, 2).Style.NumberFormat.Format);
        Assert.Equal(result.Summary.TotalCost, (decimal)summary.Cell(11, 2).GetDouble());

        var groups = wb.Worksheet("Theo kỳ và hệ thống");
        Assert.Equal(result.Groups[0].Failures, groups.Cell(2, 4).GetValue<int>());
        Assert.Equal(2.3, groups.Cell(2, 7).GetDouble(), 1);
        Assert.Equal("0.0", groups.Cell(2, 7).Style.NumberFormat.Format);
        Assert.Equal(result.Groups[0].TotalCost, (decimal)groups.Cell(2, 8).GetDouble());
        // One data row => row 3 is the TỔNG row.
        Assert.Equal(result.Summary.Failures, groups.Cell(3, 4).GetValue<int>());
        Assert.Equal(2.3, groups.Cell(3, 7).GetDouble(), 1);
        Assert.Equal(result.Summary.TotalCost, (decimal)groups.Cell(3, 8).GetDouble());

        var details = wb.Worksheet("Chi tiết phiếu");
        Assert.Equal(result.Items[0].AssetTag, details.Cell(2, 1).GetString());
        Assert.Equal(result.Items[0].Cost!.Value, (decimal)details.Cell(2, 10).GetDouble());
        // One detail row => row 3 is the TỔNG row.
        Assert.Equal(result.Items.Sum(g => g.Cost ?? 0), (decimal)details.Cell(3, 10).GetDouble());
    }

    [Fact]
    public void ExportAction_RequiresReportsViewAndExportPolicies()
    {
        var method = typeof(ReportsController).GetMethod(nameof(ReportsController.ExportRepairs));
        Assert.NotNull(method);

        var policies = method!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => attribute.Policy)
            .ToArray();

        Assert.Contains("reports.view", policies);
        Assert.Contains("export", policies);
    }

    [Fact]
    public async Task UserWithReportsViewOnly_IsDeniedExport()
    {
        await using var db = TestHelpers.CreateContext(nameof(UserWithReportsViewOnly_IsDeniedExport));
        var user = new User { Username = "qa-repair-export", Email = "qa-repair-export@test.local", IsSuperUser = false };
        var group = new PermissionGroup { Name = "QA repair viewer", IsSystem = false };
        db.Users.Add(user);
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        db.GroupPermissions.Add(new GroupPermission
        {
            GroupId = group.Id,
            PermissionKey = "reports.view",
            Value = PermissionValue.Grant,
        });
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("local_user_id", user.Id.ToString()),
            new Claim("realm_access", "{\"roles\":[\"user\"]}"),
        }, "Test"));

        Assert.True(await EvaluateAsync(db, principal, "reports.view"));
        Assert.False(await EvaluateAsync(db, principal, "export"));
    }

    private static RepairReportResult BuildResult()
    {
        var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var completion = new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Unspecified);
        var summary = new RepairReportSummaryDto(
            Failures: 3,
            OpenCount: 1,
            CompletedCount: 2,
            MttrDays: 2.25m,
            TotalCost: 150m,
            MissingCostCount: 1,
            WarrantyCount: 1,
            WarrantyRate: 0.3333m,
            UnassignedCount: 0,
            InvalidInstallationCount: 0);
        var group = new RepairReportGroupDto(
            PeriodKey: "2026-10",
            PeriodLabel: "Tháng 10/2026",
            SystemId: null,
            SystemName: "Không thuộc hệ thống",
            InstallationStatus: "Unassigned",
            Failures: 3,
            OpenCount: 1,
            CompletedCount: 2,
            MttrDays: 2.25m,
            TotalCost: 150m,
            MissingCostCount: 1,
            WarrantyCount: 1,
            WarrantyRate: 0.3333m);
        var item = new RepairReportItemDto(
            Id: Guid.NewGuid(),
            AssetId: Guid.NewGuid(),
            AssetTag: "RR-EXPORT-001",
            AssetName: "Thiết bị kiểm thử",
            ModelName: "Model A",
            CategoryName: "Danh mục A",
            Type: nameof(AssetMaintenanceType.Repair),
            StartDate: start,
            CompletionDate: completion,
            IsOpen: false,
            Cost: 150m,
            IsWarranty: true,
            SystemId: null,
            SystemName: null,
            InstallationStatus: "Unassigned",
            PeriodKey: "2026-10");

        return new RepairReportResult(
            summary, new[] { group }, new[] { item }, "Công ty kiểm thử", "Tháng 10/2026");
    }

    private static async Task<bool> EvaluateAsync(AppDbContext db, ClaimsPrincipal principal, string permissionKey)
    {
        var requirement = new PermissionRequirement(permissionKey);
        var authContext = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement }, principal, resource: null);
        var handler = new PermissionHandler(db, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        });
        await handler.HandleAsync(authContext);
        return authContext.HasSucceeded;
    }
}
