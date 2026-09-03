using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Companies.Commands;

/// <summary>
/// [Giai đoạn 3] DELETE /api/v1/companies/{id} (extracted from CompaniesController.Delete).
/// Guards verbatim: (1) SEC-FIX S5 scope — regular user only inside their subtree, out-of-scope →
/// NOT_FOUND; (2) has-children → 400 (companies.ParentId is Restrict); (3) SEC-FIX AR-2 full
/// reference audit — 10 blockers (users/locations/departments/system-infos/components/assets/
/// consumables/accessories/licenses/asset-maintenances, IgnoreQueryFilters) → 400
/// COMPANY_IN_USE with the Vietnamese blocker list.
/// ILoggableCommand (thin log) + ICacheInvalidatingCommand (on success only).
/// </summary>
public record DeleteCompanyCommand(Guid Id, Guid CurrentUserId)
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
            ItemId = Id,
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Xóa công ty \"{response.Name}\""
        };
    }
}

public class DeleteCompanyCommandHandler : IRequestHandler<DeleteCompanyCommand, CompanyResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DeleteCompanyCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<CompanyResult> Handle(DeleteCompanyCommand request, CancellationToken cancellationToken)
    {
        // [SEC-FIX S5, 2026-08-23] Company-scoping on write: out-of-scope → 404 (hide existence).
        if (!await _companyScope.IsCompanyIdInUserScopeAsync(request.Id))
            return new CompanyResult(false, "Not found", "NOT_FOUND");

        var hasChildren = await _context.Companies.AnyAsync(x => x.ParentId == request.Id, cancellationToken);
        if (hasChildren)
            return new CompanyResult(false, "Không thể xóa công ty có công ty con.");

        // [SEC-FIX AR-2, 2026-08-24] Full reference audit (AppDbContext + information_schema on the real
        // DB): FKs referencing companies = assets/consumables/accessories/licenses (SetNull),
        // components (Restrict), departments/system_infos/users (SetNull), companies.ParentId
        // (Restrict — covered by the hasChildren check above); locations.CompanyId and
        // asset_tag_counters.CompanyId are PLAIN COLUMNS without an FK. Previously only 6 inventory
        // tables were checked — deleting a company silently SetNull'd Departments/SystemInfos/Users
        // (the ExecuteUpdate below) and orphaned Locations, turning them into cross-company floaters.
        // Now EVERY referencing table blocks the delete, and a company still having assigned USERS is
        // blocked too (explicit decision 2026-08-24: no more silent floater-ing of assigned users).
        var blockers = new List<string>();
        if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.CompanyId == request.Id, cancellationToken)) blockers.Add("tài khoản người dùng");
        if (await _context.Locations.IgnoreQueryFilters().AnyAsync(l => l.CompanyId == request.Id, cancellationToken)) blockers.Add("địa điểm");
        if (await _context.Departments.IgnoreQueryFilters().AnyAsync(d => d.CompanyId == request.Id, cancellationToken)) blockers.Add("phòng ban");
        if (await _context.SystemInfos.IgnoreQueryFilters().AnyAsync(s => s.CompanyId == request.Id, cancellationToken)) blockers.Add("hệ thống");
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == request.Id, cancellationToken)) blockers.Add("linh kiện");
        if (await _context.Assets.IgnoreQueryFilters().AnyAsync(a => a.CompanyId == request.Id, cancellationToken)) blockers.Add("tài sản");
        if (await _context.Consumables.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == request.Id, cancellationToken)) blockers.Add("vật tư");
        if (await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.CompanyId == request.Id, cancellationToken)) blockers.Add("phụ kiện");
        if (await _context.Licenses.IgnoreQueryFilters().AnyAsync(l => l.CompanyId == request.Id && l.DeletedAt == null, cancellationToken)) blockers.Add("bản quyền");
        if (await _context.AssetMaintenances.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == request.Id, cancellationToken)) blockers.Add("bảo trì");
        if (blockers.Count > 0)
            return new CompanyResult(false,
                $"Công ty đang được sử dụng bởi: {string.Join(", ", blockers)} — không thể xóa.",
                "COMPANY_IN_USE");

        var c = await _context.Companies.FindAsync(request.Id);
        if (c == null)
            return new CompanyResult(false, "Not found", "NOT_FOUND");

        _context.Companies.Remove(c);
        await _context.SaveChangesAsync(cancellationToken);

        return new CompanyResult(true, "Deleted", CompanyId: request.Id, Name: c.Name);
    }
}
