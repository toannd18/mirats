using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Companies.Commands;

/// <summary>
/// [Giai đoạn 3] POST /api/v1/companies (extracted from CompaniesController.Create).
/// Validation verbatim: Code — admin may supply; otherwise auto-suggested from the name
/// (SuggestCodeAsync: strip diacritics, letters preferred, up to 4 chars, numeric suffix on
/// collision, never "NOCO"); "NOCO" is reserved (asset floater code) → 400; duplicate code → 400.
/// ILoggableCommand (thin log) + ICacheInvalidatingCommand (ref:companies, evict only on success —
/// mirrors the old early-return-before-invalidate flow).
/// </summary>
public record CreateCompanyCommand(string Name, Guid? ParentId, string? Code, Guid CurrentUserId)
    : IRequest<CompanyResult>, ILoggableCommand<CompanyResult>, ICacheInvalidatingCommand<CompanyResult>
{
    public IEnumerable<string> CacheTagsToInvalidate => new[] { CacheTags.Companies };
    public bool ShouldInvalidateCache(CompanyResult response) => response.Success;

    public ActionLogEntry? BuildLogEntry(CompanyResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Company,
            ItemId = response.CompanyId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Tạo công ty \"{response.Name}\" (mã {response.Code})"
        };
    }
}

public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, CompanyResult>
{
    private readonly IApplicationDbContext _context;

    public CreateCompanyCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CompanyResult> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
        // [Task ASSET-TAG-AUTO] Code: admin may supply; otherwise auto-suggest from name.
        var code = string.IsNullOrWhiteSpace(request.Code) ? await SuggestCodeAsync(request.Name, cancellationToken) : request.Code.Trim().ToUpperInvariant();
        if (code == "NOCO")
            return new CompanyResult(false, "\"NOCO\" là mã dành riêng cho tài sản không thuộc công ty, không được dùng.");
        if (await _context.Companies.AnyAsync(c => c.Code == code, cancellationToken))
            return new CompanyResult(false, $"Mã công ty '{code}' đã tồn tại.");

        var c = new Company { Name = request.Name, Code = code, ParentId = request.ParentId };
        _context.Companies.Add(c);
        await _context.SaveChangesAsync(cancellationToken);

        return new CompanyResult(true, "Created", CompanyId: c.Id, Name: c.Name, Code: c.Code, ParentId: c.ParentId);
    }

    /// <summary>Auto-suggests a short unique company code from the name (Task ASSET-TAG-AUTO).
    /// Reuses the Manufacturer-style algorithm: uppercase letters/digits, strip diacritics, keep up to
    /// 4 chars; append a numeric suffix when the base is taken; never "NOCO" (reserved for floaters).</summary>
    private async Task<string> SuggestCodeAsync(string name, CancellationToken cancellationToken)
    {
        var baseCode = StripDiacritics(name).Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in baseCode) if (char.IsLetter(ch)) sb.Append(ch); // prefer letters for readability
        if (sb.Length == 0) sb.Append("CO");
        var letters = sb.ToString();
        var basePart = letters.Length > 4 ? letters[..4] : letters;

        var candidate = basePart;
        if (candidate == "NOCO") candidate = "CO" + candidate;
        var suffix = 2;
        while (await _context.Companies.AnyAsync(c => c.Code == candidate, cancellationToken) || candidate == "NOCO")
        {
            var suffixStr = suffix.ToString();
            var prefixLen = Math.Max(0, 4 - suffixStr.Length);
            candidate = basePart[..Math.Min(basePart.Length, prefixLen)] + suffixStr;
            if (candidate == "NOCO") candidate = basePart + suffixStr;
            suffix++;
        }
        return candidate;
    }

    private static string StripDiacritics(string s)
    {
        var normalized = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
