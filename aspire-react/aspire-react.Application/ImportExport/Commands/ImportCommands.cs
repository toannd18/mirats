using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.ImportExport.Commands;

/// <summary>
/// Typed outcome for the 8 import endpoints — mirrors the pre-migration controller return options
/// EXACTLY: Ok(ImportSheetResult) / 400 without error_code (file guards) / 400 COMPANY_REQUIRED
/// (with error_code) / 403 Forbid (out-of-scope or nonexistent target company — Task IMPORT-T5).
/// </summary>
public record ImportOutcome(
    bool Success,
    bool Forbidden = false,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    ImportSheetResult? Result = null);

/// <summary>
/// Shared guards for the import commands — the VERBATIM move of ImportExportController
/// ResolveImportCompanyIdAsync + ValidateFile. GUARD ORDER IS LOAD-BEARING and preserved per
/// endpoint: the 7 company-targeted imports check COMPANY FIRST (a request with both a bad
/// company AND a bad file gets COMPANY_REQUIRED, like before); system-positions checks FILE
/// FIRST (it takes no company choice — B0.4 company inheritance). These two guards are the ONLY
/// logic in the import handlers besides the direct IExcelImportService call and the verbatim
/// result return — 0 other transform/filter/business logic (user-mandated no-op audit, see
/// section report).
/// </summary>
internal static class ImportGuards
{
    /// <summary>[Task IMPORT-T5] companyId mandatory (superuser must pick too — no floater) + must
    /// lie in the acting user's REAL scope. Guid.Empty → COMPANY_REQUIRED; out-of-scope or
    /// nonexistent → Forbidden (403, not 404 — authorization violation, approved decision).</summary>
    internal static async Task<ImportOutcome?> ValidateCompanyAsync(
        ICompanyScopeService companyScope, Guid companyId)
    {
        if (companyId == Guid.Empty)
            return new ImportOutcome(false, ErrorCode: "COMPANY_REQUIRED", ErrorMessage: "Phải chọn công ty cho lần import này.");
        if (!await companyScope.IsCompanyIdInUserScopeAsync(companyId))
            return new ImportOutcome(false, Forbidden: true);
        return null;
    }

    /// <summary>File guard verbatim: null/empty → "No file provided."; extension != .xlsx →
    /// "Chỉ hỗ trợ file .xlsx." (400 WITHOUT error_code — both verbatim).</summary>
    internal static ImportOutcome? ValidateFile(Stream? stream, string? fileName)
    {
        if (stream == null || stream.Length == 0)
            return new ImportOutcome(false, ErrorMessage: "No file provided.");
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext != ".xlsx")
            return new ImportOutcome(false, ErrorMessage: "Chỉ hỗ trợ file .xlsx.");
        return null;
    }
}

// ─── The 7 company-targeted imports (company-guard FIRST, then file-guard, then direct delegate) ───

public record ImportReferenceCommand(Stream? XlsxStream, string? FileName, Guid CompanyId, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

public record ImportAssetModelsCommand(Stream? XlsxStream, string? FileName, Guid CompanyId, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

public record ImportAssetsCommand(Stream? XlsxStream, string? FileName, Guid CompanyId, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

public record ImportComponentsCommand(Stream? XlsxStream, string? FileName, Guid CompanyId, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

public record ImportAccessoriesCommand(Stream? XlsxStream, string? FileName, Guid CompanyId, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

public record ImportConsumablesCommand(Stream? XlsxStream, string? FileName, Guid CompanyId, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

public record ImportSystemsCommand(Stream? XlsxStream, string? FileName, Guid CompanyId, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

// ─── SystemPositions: NO company choice (B0.4: position inherits parent SystemInfo's CompanyId;
//     the server derives the acting user's company scope itself and validates every referenced
//     parent against it inside the import service). FILE-guard FIRST (verbatim order). ───

public record ImportSystemPositionsCommand(Stream? XlsxStream, string? FileName, Guid CurrentUserId)
    : IRequest<ImportOutcome>;

/// <summary>
/// Base plumbing for the 7 company-targeted imports: company-guard → file-guard → DIRECT call to
/// the single IExcelImportService method with the exact same parameter tuple the controller used →
/// verbatim ImportOutcome. NO other logic (see ImportGuards doc). Public because MediatR handlers
/// (public) derive from it.
/// </summary>
public abstract class CompanyImportHandlerBase
{
    protected readonly ICompanyScopeService CompanyScope;
    protected readonly IExcelImportService ExcelImport;

    protected CompanyImportHandlerBase(ICompanyScopeService companyScope, IExcelImportService excelImport)
    {
        CompanyScope = companyScope;
        ExcelImport = excelImport;
    }

    protected async Task<ImportOutcome> RunAsync(
        Stream? stream, string? fileName, Guid companyId, Guid currentUserId,
        Func<Stream, Guid, Guid, CancellationToken, Task<ImportSheetResult>> call,
        CancellationToken cancellationToken)
    {
        var companyError = await ImportGuards.ValidateCompanyAsync(CompanyScope, companyId);
        if (companyError != null) return companyError;
        var fileError = ImportGuards.ValidateFile(stream, fileName);
        if (fileError != null) return fileError;

        var result = await call(stream!, currentUserId, companyId, cancellationToken);
        return new ImportOutcome(true, Result: result);
    }
}

public class ImportReferenceCommandHandler : CompanyImportHandlerBase, IRequestHandler<ImportReferenceCommand, ImportOutcome>
{
    public ImportReferenceCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
        : base(companyScope, excelImport) { }

    public Task<ImportOutcome> Handle(ImportReferenceCommand request, CancellationToken cancellationToken)
        => RunAsync(request.XlsxStream, request.FileName, request.CompanyId, request.CurrentUserId,
            (s, u, c, ct) => ExcelImport.ImportReferenceAsync(s, u, c, ct), cancellationToken);
}

public class ImportAssetModelsCommandHandler : CompanyImportHandlerBase, IRequestHandler<ImportAssetModelsCommand, ImportOutcome>
{
    public ImportAssetModelsCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
        : base(companyScope, excelImport) { }

    public Task<ImportOutcome> Handle(ImportAssetModelsCommand request, CancellationToken cancellationToken)
        => RunAsync(request.XlsxStream, request.FileName, request.CompanyId, request.CurrentUserId,
            (s, u, c, ct) => ExcelImport.ImportAssetModelsAsync(s, u, c, ct), cancellationToken);
}

public class ImportAssetsCommandHandler : CompanyImportHandlerBase, IRequestHandler<ImportAssetsCommand, ImportOutcome>
{
    public ImportAssetsCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
        : base(companyScope, excelImport) { }

    public Task<ImportOutcome> Handle(ImportAssetsCommand request, CancellationToken cancellationToken)
        => RunAsync(request.XlsxStream, request.FileName, request.CompanyId, request.CurrentUserId,
            (s, u, c, ct) => ExcelImport.ImportAssetsAsync(s, u, c, ct), cancellationToken);
}

public class ImportComponentsCommandHandler : CompanyImportHandlerBase, IRequestHandler<ImportComponentsCommand, ImportOutcome>
{
    public ImportComponentsCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
        : base(companyScope, excelImport) { }

    public Task<ImportOutcome> Handle(ImportComponentsCommand request, CancellationToken cancellationToken)
        => RunAsync(request.XlsxStream, request.FileName, request.CompanyId, request.CurrentUserId,
            (s, u, c, ct) => ExcelImport.ImportComponentsAsync(s, u, c, ct), cancellationToken);
}

public class ImportAccessoriesCommandHandler : CompanyImportHandlerBase, IRequestHandler<ImportAccessoriesCommand, ImportOutcome>
{
    public ImportAccessoriesCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
        : base(companyScope, excelImport) { }

    public Task<ImportOutcome> Handle(ImportAccessoriesCommand request, CancellationToken cancellationToken)
        => RunAsync(request.XlsxStream, request.FileName, request.CompanyId, request.CurrentUserId,
            (s, u, c, ct) => ExcelImport.ImportAccessoriesAsync(s, u, c, ct), cancellationToken);
}

public class ImportConsumablesCommandHandler : CompanyImportHandlerBase, IRequestHandler<ImportConsumablesCommand, ImportOutcome>
{
    public ImportConsumablesCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
        : base(companyScope, excelImport) { }

    public Task<ImportOutcome> Handle(ImportConsumablesCommand request, CancellationToken cancellationToken)
        => RunAsync(request.XlsxStream, request.FileName, request.CompanyId, request.CurrentUserId,
            (s, u, c, ct) => ExcelImport.ImportConsumablesAsync(s, u, c, ct), cancellationToken);
}

public class ImportSystemsCommandHandler : CompanyImportHandlerBase, IRequestHandler<ImportSystemsCommand, ImportOutcome>
{
    public ImportSystemsCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
        : base(companyScope, excelImport) { }

    public Task<ImportOutcome> Handle(ImportSystemsCommand request, CancellationToken cancellationToken)
        => RunAsync(request.XlsxStream, request.FileName, request.CompanyId, request.CurrentUserId,
            (s, u, c, ct) => ExcelImport.ImportSystemsAsync(s, u, c, ct), cancellationToken);
}

public class ImportSystemPositionsCommandHandler : IRequestHandler<ImportSystemPositionsCommand, ImportOutcome>
{
    private readonly ICompanyScopeService _companyScope;
    private readonly IExcelImportService _excelImport;

    public ImportSystemPositionsCommandHandler(ICompanyScopeService companyScope, IExcelImportService excelImport)
    {
        _companyScope = companyScope;
        _excelImport = excelImport;
    }

    public async Task<ImportOutcome> Handle(ImportSystemPositionsCommand request, CancellationToken cancellationToken)
    {
        // Verbatim order: FILE-guard FIRST (no company choice to validate — B0.4 inheritance),
        // then the acting user's company scope is derived HERE (was controller-side before — same
        // ICompanyScopeService call, same value) and passed to the service.
        var fileError = ImportGuards.ValidateFile(request.XlsxStream, request.FileName);
        if (fileError != null) return fileError;
        var actingUserCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var result = await _excelImport.ImportSystemPositionsAsync(request.XlsxStream!, request.CurrentUserId, actingUserCompanyId, cancellationToken);
        return new ImportOutcome(true, Result: result);
    }
}
