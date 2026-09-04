using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceTemplates.Commands;

/// <summary>
/// Typed staging helper — the verbatim port of the controller's LogTemplateAction: ItemType
/// MaintenanceChecklistTemplate, TargetSystemInfoId/Name snapshot from the template, LogMeta
/// serialized with DEFAULT options (verbatim — NOT UnsafeRelaxedJsonEscaping), CompanyId from
/// the template. Persisted via IActionLogService.Log in the handler's own SaveChanges (the
/// controller's 2-step Save pattern: data first, log second — kept verbatim).
/// </summary>
internal static class MaintenanceTemplateLogging
{
    internal static ActionLogEntry Build(
        ActionType actionType,
        MaintenanceChecklistTemplate template,
        Guid currentUserId,
        string note,
        object? meta = null,
        Guid itemId = default)
    {
        return new ActionLogEntry
        {
            ItemType = ItemType.MaintenanceChecklistTemplate,
            ItemId = itemId == default ? template.Id : itemId,
            ActionType = actionType,
            CreatedBy = currentUserId,
            CompanyId = template.CompanyId,
            TargetSystemInfoId = template.SystemInfoId,
            TargetSystemInfoName = template.SystemInfo?.Name,
            LogMeta = meta == null ? null : JsonSerializer.Serialize(meta),
            Note = note
        };
    }
}

// ─── CREATE ───

public record CreateMaintenanceTemplateResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? TemplateId = null,
    string? Name = null,
    Guid? SystemInfoId = null,
    Guid? CompanyId = null,
    bool IsActive = false,
    Guid? InitialVersionId = null);

public record CreateMaintenanceTemplateCommand(string? Name, Guid? SystemInfoId, Guid? CompanyId, Guid CurrentUserId)
    : IRequest<CreateMaintenanceTemplateResult>;

public class CreateMaintenanceTemplateCommandHandler : IRequestHandler<CreateMaintenanceTemplateCommand, CreateMaintenanceTemplateResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CreateMaintenanceTemplateCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<CreateMaintenanceTemplateResult> Handle(CreateMaintenanceTemplateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return new CreateMaintenanceTemplateResult(false, "Tên template là bắt buộc.", "NAME_REQUIRED");
        var name = request.Name.Trim();
        if (request.SystemInfoId is not { } systemInfoId || systemInfoId == Guid.Empty)
            return new CreateMaintenanceTemplateResult(false, "Hệ thống áp dụng (SystemInfoId) là bắt buộc.", "SYSTEM_INFO_REQUIRED");

        // The template lives under a system the creator can see (404 hides out-of-scope systems).
        if (!await MaintenanceTemplateAccess.IsSystemVisibleAsync(_context, _companyScope, systemInfoId))
            return new CreateMaintenanceTemplateResult(false, "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn.", "NOT_FOUND");

        // [Task L2 — COMPANY-SCOPING on Create] Never trust client CompanyId: a regular user may only
        // create for their own company (or omit → floater). Superuser may target any existing company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (request.CompanyId.HasValue)
        {
            if (!await _context.Companies.AsNoTracking().AnyAsync(c => c.Id == request.CompanyId.Value, cancellationToken))
                return new CreateMaintenanceTemplateResult(false, "Công ty không hợp lệ.", "INVALID_COMPANY");
            if (userCompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
                return new CreateMaintenanceTemplateResult(false, "Bạn chỉ được tạo template cho công ty của mình.", "COMPANY_MISMATCH");
        }

        // Unique (SystemInfoId, Name) — checked explicitly so the client gets a clean 400 instead of a raw Postgres unique violation (500).
        if (await _context.MaintenanceChecklistTemplates.AnyAsync(t => t.SystemInfoId == systemInfoId && t.Name == name, cancellationToken))
            return new CreateMaintenanceTemplateResult(false, "Tên template đã tồn tại trong hệ thống này.", "TEMPLATE_NAME_TAKEN");

        var userId = request.CurrentUserId;
        var template = new MaintenanceChecklistTemplate
        {
            Name = name,
            SystemInfoId = systemInfoId,
            CompanyId = request.CompanyId,
            CreatedById = userId
        };
        // Draft version 1 — born unpublished; items/params are added to it before publishing.
        var draft = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = template.Id,
            VersionNumber = 1,
            CreatedById = userId
        };
        template.Versions.Add(draft);
        _context.MaintenanceChecklistTemplates.Add(template);
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Create, template, userId,
            $"Tạo template bảo dưỡng \"{template.Name}\"",
            new { draftVersionId = draft.Id, versionNumber = 1 }));
        await _context.SaveChangesAsync(cancellationToken);

        return new CreateMaintenanceTemplateResult(true, "Created.",
            TemplateId: template.Id, Name: template.Name, SystemInfoId: template.SystemInfoId,
            CompanyId: template.CompanyId, IsActive: template.IsActive, InitialVersionId: draft.Id);
    }
}

// ─── UPDATE ───

public record UpdateMaintenanceTemplateResult(bool Success, string Message, string? ErrorCode = null);

public record UpdateMaintenanceTemplateCommand(
    Guid Id, string? Name, Guid? SystemInfoId, Guid? CompanyId, bool? IsActive, Guid CurrentUserId)
    : IRequest<UpdateMaintenanceTemplateResult>;

public class UpdateMaintenanceTemplateCommandHandler : IRequestHandler<UpdateMaintenanceTemplateCommand, UpdateMaintenanceTemplateResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpdateMaintenanceTemplateCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<UpdateMaintenanceTemplateResult> Handle(UpdateMaintenanceTemplateCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.Id);
        if (t == null)
            return new UpdateMaintenanceTemplateResult(false, "Not found.", "NOT_FOUND");

        // CompanyId is intentionally NOT updatable (re-scoping would silently move visibility of every
        // campaign pinned through its versions) — explicit change attempts get FIELD_LOCKED.
        if (request.CompanyId.HasValue && request.CompanyId.Value != t.CompanyId)
            return new UpdateMaintenanceTemplateResult(false, "Không thể đổi công ty sau khi tạo template.", "FIELD_LOCKED");

        // Patch-aware: a field counts as changed ONLY when it was explicitly sent AND differs
        // (Task F/M1 pattern — an absent field must never be treated as changed).
        var newName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var nameChanged = newName != null && newName != t.Name;
        var sysChanged = request.SystemInfoId.HasValue && request.SystemInfoId.Value != t.SystemInfoId;
        var activeChanged = request.IsActive.HasValue && request.IsActive.Value != t.IsActive;

        // SystemInfoId locked once any campaign pins one of this template's versions (moving systems
        // would orphan the historical context those campaigns were run against).
        if (sysChanged && await MaintenanceTemplateAccess.TemplateHasCampaignsAsync(_context, request.Id))
            return new UpdateMaintenanceTemplateResult(false, "Template đã có đợt bảo dưỡng tham chiếu — không thể đổi hệ thống.", "FIELD_LOCKED");

        if (nameChanged || sysChanged)
        {
            var effectiveSystemId = sysChanged ? request.SystemInfoId!.Value : t.SystemInfoId;
            if (sysChanged && !await MaintenanceTemplateAccess.IsSystemVisibleAsync(_context, _companyScope, effectiveSystemId))
                return new UpdateMaintenanceTemplateResult(false, "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn.", "NOT_FOUND");
            // Unique (SystemInfoId, Name) — explicit check → clean 400, not a raw Postgres unique violation.
            if (await _context.MaintenanceChecklistTemplates.AnyAsync(x =>
                    x.Id != request.Id && x.SystemInfoId == effectiveSystemId &&
                    x.Name == (nameChanged ? newName : t.Name), cancellationToken))
                return new UpdateMaintenanceTemplateResult(false, "Tên template đã tồn tại trong hệ thống này.", "TEMPLATE_NAME_TAKEN");
        }

        if (!nameChanged && !sysChanged && !activeChanged)
            return new UpdateMaintenanceTemplateResult(true, "Updated."); // nothing actually sent/changed

        // Capture BEFORE values prior to mutation (LogMeta.changes must hold true olds).
        var beforeName = t.Name;
        var beforeSystemId = t.SystemInfoId;
        var beforeIsActive = t.IsActive;

        if (nameChanged) t.Name = newName!;
        if (sysChanged) t.SystemInfoId = request.SystemInfoId!.Value;
        if (activeChanged) t.IsActive = request.IsActive!.Value;
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Update, t, request.CurrentUserId,
            $"Cập nhật template bảo dưỡng \"{t.Name}\"", new
            {
                changes = new
                {
                    name = new { old = beforeName, @new = t.Name },
                    systemInfoId = new { old = beforeSystemId, @new = t.SystemInfoId },
                    isActive = new { old = beforeIsActive, @new = t.IsActive }
                }
            }));
        await _context.SaveChangesAsync(cancellationToken);
        return new UpdateMaintenanceTemplateResult(true, "Updated.");
    }
}

// ─── DELETE ───

public record DeleteMaintenanceTemplateCommand(Guid Id, Guid CurrentUserId) : IRequest<DeleteMaintenanceTemplateResult>;

public class DeleteMaintenanceTemplateCommandHandler : IRequestHandler<DeleteMaintenanceTemplateCommand, DeleteMaintenanceTemplateResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteMaintenanceTemplateCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<DeleteMaintenanceTemplateResult> Handle(DeleteMaintenanceTemplateCommand request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.Id);
        if (t == null)
            return new DeleteMaintenanceTemplateResult(false, "Not found.", "NOT_FOUND");

        // Delete-guard by usage history: any campaign pinning ANY version of this template blocks the
        // hard delete (DB-level the campaign→version FK is RESTRICT — we surface it as a clean 400).
        if (await MaintenanceTemplateAccess.TemplateHasCampaignsAsync(_context, request.Id))
            return new DeleteMaintenanceTemplateResult(false, "Template đang có đợt bảo dưỡng tham chiếu — không thể xóa.", "TEMPLATE_IN_USE");

        var name = t.Name;
        _context.MaintenanceChecklistTemplates.Remove(t); // versions/items/params cascade (config-only, no history left behind)
        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(MaintenanceTemplateLogging.Build(
            ActionType.Delete, t, request.CurrentUserId, $"Xóa template bảo dưỡng \"{name}\""));
        await _context.SaveChangesAsync(cancellationToken);
        return new DeleteMaintenanceTemplateResult(true, "Deleted.");
    }
}

public record DeleteMaintenanceTemplateResult(bool Success, string Message, string? ErrorCode = null);
