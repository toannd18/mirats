namespace aspire_react.Server.Application.ImportExport;

/// <summary>Row-level outcome of one imported row (best-effort import: a bad row is reported, not fatal).</summary>
public sealed record ImportRowResult(int RowNumber, bool Success, string Message);

/// <summary>Aggregated outcome of one sheet import. <c>Rows</c> = every processed row (for the
/// per-row UI report), <c>Errors</c> = the failed subset.</summary>
public sealed record ImportSheetResult(int Created, int Failed, IReadOnlyList<ImportRowResult> Rows, IReadOnlyList<ImportRowResult> Errors);

/// <summary>
/// Shared Excel (.xlsx) import machinery for the Mirats import feature (T1–T4).
/// [Giai đoạn 3 — ImportExport] Interface + result records MOVED verbatim from
/// Infrastructure/Services/ExcelImportService.cs to Application (pattern Jason Taylor, same as
/// IComponentAllocationService/IConsumableAllocationService): import command handlers (Application)
/// consume the contract while the ClosedXML implementation (ExcelImportService) stays in
/// Infrastructure — compiler-enforced dependency direction. Implementation + DI registration are
/// UNTOUCHED (InfrastructureServiceCollectionExtensions.AddInfrastructureServices).
/// Design decisions (approved, unchanged):
///  - Sheet lookup BY NAME (not position) — mirrors the sample workbook
///    <c>docs/Mirats_DuLieuMau_VatTu_T&amp;E.xlsx</c> (1_DanhMuc…7_VatTuTieuHao).
///  - Header row is auto-detected and each column is located by its header text — column order
///    in the file does not matter.
///  - Best-effort per row (no all-or-nothing atomicity).
///  - Reference sheets (T1) auto-create Category/Manufacturer/Location when missing;
///    inventory sheets resolve by name only and report a per-row error if missing
///    (AssetModel is NEVER auto-created — approved decision).
///  - Component serial rows are grouped by (Name + CategoryName + ModelNumber) into ONE
///    serial-tracked component; StockInAsync is reused for per-unit stock + audit logging.
///  - Every created record gets its own ActionLog (ActionType.Import) in the same SaveChanges.
///  - Company scoping (Task IMPORT-T5): ONE import = ONE company. The controller validates the
///    client-supplied target company against the acting user's real scope (never trust the client)
///    and passes it in; every created record AND every ActionLog of the batch gets that CompanyId.
///    Category/Manufacturer stay global entities (no CompanyId column) but their import ActionLogs
///    are stamped with the chosen company so the whole batch is auditable per company.
/// </summary>
public interface IExcelImportService
{
    Task<ImportSheetResult> ImportReferenceAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportAssetModelsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportAssetsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportComponentsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportAccessoriesAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportConsumablesAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportSystemsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportSystemPositionsAsync(Stream xlsxStream, Guid actingUserId, Guid? actingUserCompanyId, CancellationToken ct = default);
}
