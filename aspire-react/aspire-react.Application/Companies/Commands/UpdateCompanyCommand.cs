using System.Text.Json;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Companies.Commands;

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/companies/{id} (extracted from CompaniesController.Update — SEC-FIX S5).
/// Company-scoping verbatim: regular user may only update companies inside their subtree
/// (own + descendants); superuser → any; out-of-scope → NOT_FOUND (hide existence).
/// Semantics verbatim: Name/ParentId assigned unconditionally (full-put for those two — a null
/// parentId re-roots the company); Code keeps its old value when the request sends whitespace.
/// NOCO reserved → 400; duplicate code (when changed) → 400; circular re-parent (self or any
/// descendant as parent) → 400 (GetDescendantIdsAsync BFS — verbatim).
/// ILoggableCommand (LogMeta ×3: name/code/parentId) + ICacheInvalidatingCommand (on success only).
/// </summary>
public record UpdateCompanyCommand(Guid Id, string Name, Guid? ParentId, string? Code, Guid CurrentUserId)
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
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateCompanyCommandHandler : IRequestHandler<UpdateCompanyCommand, CompanyResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public UpdateCompanyCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<CompanyResult> Handle(UpdateCompanyCommand request, CancellationToken cancellationToken)
    {
        // [SEC-FIX S5, 2026-08-23] Company-scoping on write: out-of-scope → 404 (hide existence).
        if (!await _companyScope.IsCompanyIdInUserScopeAsync(request.Id))
            return new CompanyResult(false, "Not found", "NOT_FOUND");

        var c = await _context.Companies.FindAsync(request.Id);
        if (c == null)
            return new CompanyResult(false, "Not found", "NOT_FOUND");

        var before = new { c.Name, c.Code, c.ParentId };
        c.Name = request.Name;
        c.ParentId = request.ParentId;
        // Code is editable on update; validate NOCO + uniqueness when it changes.
        var code = string.IsNullOrWhiteSpace(request.Code) ? c.Code : request.Code.Trim().ToUpperInvariant();
        if (code == "NOCO")
            return new CompanyResult(false, "\"NOCO\" là mã dành riêng cho tài sản không thuộc công ty, không được dùng.");
        if (code != c.Code && await _context.Companies.AnyAsync(x => x.Code == code && x.Id != request.Id, cancellationToken))
            return new CompanyResult(false, $"Mã công ty '{code}' đã tồn tại.");
        c.Code = code;
        // Prevent circular reference: cannot set parent to itself or its children
        if (request.ParentId.HasValue)
        {
            var descendantIds = await GetDescendantIdsAsync(request.Id, cancellationToken);
            if (request.ParentId == request.Id || descendantIds.Contains(request.ParentId.Value))
                return new CompanyResult(false, "Không thể chọn chính nó hoặc công ty con làm cha.");
        }
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                name = new { old = before.Name, @new = c.Name },
                code = new { old = before.Code, @new = c.Code },
                parentId = new { old = before.ParentId, @new = c.ParentId }
            }
        });

        return new CompanyResult(true, "Updated",
            CompanyId: c.Id, Name: c.Name,
            LogMeta: logMeta, Note: $"Cập nhật công ty \"{c.Name}\"");
    }

    private async Task<HashSet<Guid>> GetDescendantIdsAsync(Guid parentId, CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(parentId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await _context.Companies.Where(c => c.ParentId == current).Select(c => c.Id).ToListAsync(cancellationToken);
            foreach (var childId in children)
            {
                if (result.Add(childId)) queue.Enqueue(childId);
            }
        }
        return result;
    }
}
