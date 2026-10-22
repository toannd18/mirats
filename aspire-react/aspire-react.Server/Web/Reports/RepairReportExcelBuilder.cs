using ClosedXML.Excel;
using aspire_react.Server.Application.Reports.RepairReports;

namespace aspire_react.Server.Web.Reports;

/// <summary>
/// [REPAIR-REPORT] Excel presentation only. It consumes the exact result produced by
/// RepairReportQueryHandler (same filters + same SQL aggregation as the screen/print page).
/// </summary>
public static class RepairReportExcelBuilder
{
    public static byte[] Build(RepairReportResult result, DateTime exportedAt)
    {
        using var wb = new XLWorkbook();
        BuildSummarySheet(wb, result, exportedAt);
        BuildGroupsSheet(wb, result);
        BuildDetailsSheet(wb, result);
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildSummarySheet(XLWorkbook wb, RepairReportResult result, DateTime exportedAt)
    {
        var ws = wb.AddWorksheet("Tổng hợp");
        ws.Cell(1, 1).Value = "BÁO CÁO HỎNG HÓC – SỬA CHỮA";
        ws.Range(1, 1, 1, 3).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(2, 1).Value = "Đơn vị:";
        ws.Cell(2, 2).Value = result.ScopeCompanyName;
        ws.Cell(3, 1).Value = "Bộ lọc:";
        ws.Range(3, 2, 3, 3).Merge();
        ws.Cell(3, 2).Value = result.FilterSummary;
        ws.Cell(3, 2).Style.Alignment.WrapText = true;
        ws.Cell(4, 1).Value = "Thời điểm xuất:";
        ws.Cell(4, 2).Value = exportedAt;
        ws.Cell(4, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

        ws.Cell(6, 1).Value = "Chỉ số";
        ws.Cell(6, 2).Value = "Giá trị";
        ws.Cell(6, 3).Value = "Ghi chú";
        ws.Range(6, 1, 6, 3).Style.Font.Bold = true;
        ws.Range(6, 1, 6, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF2FF");

        var rows = new (string Label, object? Value, string? Format, string Note)[]
        {
            ("Số lần hỏng", result.Summary.Failures, "0", "Bao gồm phiếu chưa hoàn thành"),
            ("Đang mở", result.Summary.OpenCount, "0", "Chưa có ngày hoàn thành; không tính MTTR"),
            ("Đã hoàn thành", result.Summary.CompletedCount, "0", ""),
            ("MTTR (ngày)", RoundMttr(result.Summary.MttrDays), "0.0", "Chỉ phiếu đã hoàn thành; theo ngày VN"),
            ("Tổng chi phí", result.Summary.TotalCost, "#,##0", "Tổng các phiếu có nhập chi phí"),
            ("Số phiếu thiếu chi phí", result.Summary.MissingCostCount, "0", ""),
            ("Số phiếu bảo hành", result.Summary.WarrantyCount, "0", "Theo cờ IsWarranty"),
            ("Tỷ lệ bảo hành", result.Summary.WarrantyRate, "0.0%", "Số phiếu bảo hành / số lần hỏng"),
            ("Không thuộc hệ thống", result.Summary.UnassignedCount, "0", "Không có lý lịch lắp đặt phù hợp"),
            ("Dữ liệu lý lịch không hợp lệ", result.Summary.InvalidInstallationCount, "0", "Nhiều khoảng lắp cùng khớp"),
        };

        var row = 7;
        foreach (var item in rows)
        {
            ws.Cell(row, 1).Value = item.Label;
            ws.Cell(row, 2).Value = item.Value switch
            {
                null => "—",
                decimal d => (double)d,
                int i => i,
                _ => item.Value?.ToString() ?? "—",
            };
            if (item.Format is not null) ws.Cell(row, 2).Style.NumberFormat.Format = item.Format;
            ws.Cell(row, 3).Value = item.Note;
            row++;
        }

        ws.Cell(row + 1, 1).Value = "Định nghĩa: Hệ thống theo lý lịch lắp đặt tại 00:00 ngày bắt đầu (giờ VN); bảo hành theo cờ IsWarranty; MTTR theo ngày, chỉ tính phiếu đã hoàn thành.";
        ws.Range(row + 1, 1, row + 1, 3).Merge();
        ws.Cell(row + 1, 1).Style.Alignment.WrapText = true;
        ws.Columns(1, 3).AdjustToContents();
        ws.Column(1).Width = 32;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 55;
        ws.SheetView.FreezeRows(6);
    }

    private static void BuildGroupsSheet(XLWorkbook wb, RepairReportResult result)
    {
        var ws = wb.AddWorksheet("Theo kỳ và hệ thống");
        string[] headers = [
            "Kỳ", "Hệ thống", "Trạng thái lý lịch", "Số lần hỏng", "Đang mở", "Đã hoàn thành",
            "MTTR (ngày)", "Tổng chi phí", "Thiếu chi phí", "Bảo hành", "Tỷ lệ bảo hành",
        ];
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF2FF");

        var row = 2;
        foreach (var g in result.Groups)
        {
            ws.Cell(row, 1).Value = g.PeriodLabel;
            ws.Cell(row, 2).Value = g.SystemName;
            ws.Cell(row, 3).Value = StatusLabel(g.InstallationStatus);
            ws.Cell(row, 4).Value = g.Failures;
            ws.Cell(row, 5).Value = g.OpenCount;
            ws.Cell(row, 6).Value = g.CompletedCount;
            var groupMttr = RoundMttr(g.MttrDays);
            ws.Cell(row, 7).Value = groupMttr is null ? "—" : (double)groupMttr.Value;
            if (groupMttr is not null) ws.Cell(row, 7).Style.NumberFormat.Format = "0.0";
            ws.Cell(row, 8).Value = (double)g.TotalCost;
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 9).Value = g.MissingCostCount;
            ws.Cell(row, 10).Value = g.WarrantyCount;
            ws.Cell(row, 11).Value = (double)g.WarrantyRate;
            ws.Cell(row, 11).Style.NumberFormat.Format = "0.0%";
            row++;
        }

        ws.Cell(row, 1).Value = "TỔNG";
        ws.Cell(row, 4).Value = result.Summary.Failures;
        ws.Cell(row, 5).Value = result.Summary.OpenCount;
        ws.Cell(row, 6).Value = result.Summary.CompletedCount;
        var summaryMttr = RoundMttr(result.Summary.MttrDays);
        ws.Cell(row, 7).Value = summaryMttr is null ? "—" : (double)summaryMttr.Value;
        if (summaryMttr is not null) ws.Cell(row, 7).Style.NumberFormat.Format = "0.0";
        ws.Cell(row, 8).Value = (double)result.Summary.TotalCost;
        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
        ws.Cell(row, 9).Value = result.Summary.MissingCostCount;
        ws.Cell(row, 10).Value = result.Summary.WarrantyCount;
        ws.Cell(row, 11).Value = (double)result.Summary.WarrantyRate;
        ws.Cell(row, 11).Style.NumberFormat.Format = "0.0%";
        ws.Range(row, 1, row, headers.Length).Style.Font.Bold = true;
        ws.Range(row, 1, row, headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        ws.Range(1, 1, row, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, headers.Length).AdjustToContents();
        ws.Column(2).Width = Math.Min(ws.Column(2).Width, 40);
    }

    private static void BuildDetailsSheet(XLWorkbook wb, RepairReportResult result)
    {
        var ws = wb.AddWorksheet("Chi tiết phiếu");
        string[] headers = [
            "Mã tài sản", "Tên tài sản", "Danh mục", "Model", "Loại", "Hệ thống",
            "Trạng thái lý lịch", "Bắt đầu", "Hoàn thành", "Chi phí", "Bảo hành",
        ];
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF2FF");

        var row = 2;
        foreach (var item in result.Items)
        {
            ws.Cell(row, 1).Value = item.AssetTag;
            ws.Cell(row, 2).Value = item.AssetName;
            ws.Cell(row, 3).Value = item.CategoryName ?? "—";
            ws.Cell(row, 4).Value = item.ModelName ?? "—";
            ws.Cell(row, 5).Value = TypeLabel(item.Type);
            ws.Cell(row, 6).Value = item.SystemName ?? StatusLabel(item.InstallationStatus);
            ws.Cell(row, 7).Value = StatusLabel(item.InstallationStatus);
            ws.Cell(row, 8).Value = item.StartDate;
            ws.Cell(row, 8).Style.DateFormat.Format = "dd/MM/yyyy";
            if (item.CompletionDate.HasValue)
            {
                ws.Cell(row, 9).Value = item.CompletionDate.Value;
                ws.Cell(row, 9).Style.DateFormat.Format = "dd/MM/yyyy";
            }
            else
            {
                ws.Cell(row, 9).Value = "Đang mở";
            }
            if (item.Cost.HasValue)
            {
                ws.Cell(row, 10).Value = (double)item.Cost.Value;
                ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
            }
            else
            {
                ws.Cell(row, 10).Value = "—";
            }
            ws.Cell(row, 11).Value = item.IsWarranty ? "Có" : "Không";
            row++;
        }

        ws.Cell(row, 1).Value = $"TỔNG ({result.Items.Count} phiếu)";
        ws.Cell(row, 10).Value = (double)result.Items.Where(i => i.Cost.HasValue).Sum(i => i.Cost!.Value);
        ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
        ws.Range(row, 1, row, headers.Length).Style.Font.Bold = true;
        ws.Range(row, 1, row, headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        ws.Range(1, 1, row, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, headers.Length).AdjustToContents();
        ws.Column(2).Width = Math.Min(ws.Column(2).Width, 40);
    }

    /// <summary>
    /// Keep the Excel cell value and the handler DTO identical: one decimal, away from zero.
    /// The number format ("0.0") is presentation only; storing an unrounded value would make
    /// the workbook disagree with the screen/print at the third decimal.
    /// </summary>
    private static decimal? RoundMttr(decimal? value) =>
        value is null ? null : Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);

    private static string StatusLabel(string status) => status switch
    {
        "AtStart" => "Đang lắp lúc 00:00",
        "SameDay" => "Bắt đầu trong ngày",
        "Invalid" => "Dữ liệu lý lịch không hợp lệ",
        _ => "Không thuộc hệ thống",
    };

    private static string TypeLabel(string type) => type switch
    {
        "Maintenance" => "Bảo trì định kỳ",
        "Repair" => "Sửa chữa",
        "Upgrade" => "Nâng cấp",
        "HardwareSupport" => "Hỗ trợ phần cứng",
        "SoftwareSupport" => "Hỗ trợ phần mềm",
        "PatTest" => "PAT test",
        "Calibration" => "Hiệu chuẩn",
        "IncidentReport" => "Báo cáo sự cố",
        _ => type,
    };
}
