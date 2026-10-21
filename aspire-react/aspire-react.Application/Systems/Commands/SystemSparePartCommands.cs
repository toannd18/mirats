using System.Text.Json;
using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Systems.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Commands;

public sealed record SparePartCommandResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? Id = null,
    Guid? SystemInfoId = null,
    string? ItemName = null,
    int? MinQty = null,
    Guid? CompanyId = null,
    string? LogMeta = null);

public record CreateSystemSparePartCommand(
    Guid SystemInfoId,
    Guid? ComponentId,
    Guid? ConsumableId,
    int MinQty,
    string? Note,
    Guid CurrentUserId) : IRequest<SparePartCommandResult>, ILoggableCommand<SparePartCommandResult>
{
    public ActionLogEntry? BuildLogEntry(SparePartCommandResult response)
        => response.Success && response.Id.HasValue
            ? new ActionLogEntry
            {
                ItemType = ItemType.SystemInfo,
                ItemId = response.Id.Value,
                ActionType = ActionType.Create,
                CreatedBy = CurrentUserId,
                CompanyId = response.CompanyId,
                TargetType = AssignmentTargetType.SystemInfo,
                TargetId = SystemInfoId,
                TargetSystemInfoId = SystemInfoId,
                Note = $"Thêm định mức vật tư dự phòng: {response.ItemName}",
                LogMeta = JsonSerializer.Serialize(new
                {
                    item_name = response.ItemName,
                    min_qty = response.MinQty,
                }),
            }
            : null;
}

public record UpdateSystemSparePartCommand(
    Guid SystemInfoId,
    Guid RequirementId,
    int? MinQty,
    string? Note,
    bool? IsActive,
    Guid CurrentUserId) : IRequest<SparePartCommandResult>, ILoggableCommand<SparePartCommandResult>
{
    public ActionLogEntry? BuildLogEntry(SparePartCommandResult response)
        => response.Success && response.Id.HasValue
            ? new ActionLogEntry
            {
                ItemType = ItemType.SystemInfo,
                ItemId = response.Id.Value,
                ActionType = ActionType.Update,
                CreatedBy = CurrentUserId,
                CompanyId = response.CompanyId,
                TargetType = AssignmentTargetType.SystemInfo,
                TargetId = SystemInfoId,
                TargetSystemInfoId = SystemInfoId,
                Note = $"Cập nhật định mức vật tư dự phòng: {response.ItemName}",
                LogMeta = response.LogMeta,
            }
            : null;
}

public record DeleteSystemSparePartCommand(
    Guid SystemInfoId,
    Guid RequirementId,
    Guid CurrentUserId) : IRequest<SparePartCommandResult>, ILoggableCommand<SparePartCommandResult>
{
    public ActionLogEntry? BuildLogEntry(SparePartCommandResult response)
        => response.Success && response.Id.HasValue
            ? new ActionLogEntry
            {
                ItemType = ItemType.SystemInfo,
                ItemId = response.Id.Value,
                ActionType = ActionType.Delete,
                CreatedBy = CurrentUserId,
                CompanyId = response.CompanyId,
                TargetType = AssignmentTargetType.SystemInfo,
                TargetId = SystemInfoId,
                TargetSystemInfoId = SystemInfoId,
                Note = $"Xóa định mức vật tư dự phòng: {response.ItemName}",
                LogMeta = response.LogMeta,
            }
            : null;
}

internal static class SparePartCommandSupport
{
    public static async Task<SystemInfo?> GetVisibleSystemAsync(
        IApplicationDbContext context, ICompanyScopeService scope, Guid systemId, CancellationToken ct)
    {
        var companyId = await scope.GetCurrentUserCompanyIdAsync();
        return await context.SystemInfos.FirstOrDefaultAsync(s =>
            s.Id == systemId &&
            (companyId == null || s.CompanyId == null || s.CompanyId == companyId.Value), ct);
    }

    public static async Task<(bool Ok, string? Name, string? Error)> ValidateItemAsync(
        IApplicationDbContext context, SystemInfo system, Guid? componentId, Guid? consumableId, CancellationToken ct)
    {
        if ((componentId.HasValue ? 1 : 0) + (consumableId.HasValue ? 1 : 0) != 1)
            return (false, null, "INVALID_SPARE_ITEM");

        if (componentId.HasValue)
        {
            var component = await context.Components.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == componentId.Value && c.CompanyId == system.CompanyId, ct);
            return component == null ? (false, null, "INVALID_SPARE_ITEM") : (true, component.Name, null);
        }

        var consumable = await context.Consumables.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == consumableId!.Value && c.CompanyId == system.CompanyId, ct);
        return consumable == null ? (false, null, "INVALID_SPARE_ITEM") : (true, consumable.Name, null);
    }
}

public class CreateSystemSparePartCommandHandler
    : IRequestHandler<CreateSystemSparePartCommand, SparePartCommandResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _scope;

    public CreateSystemSparePartCommandHandler(IApplicationDbContext context, ICompanyScopeService scope)
    {
        _context = context;
        _scope = scope;
    }

    public async Task<SparePartCommandResult> Handle(CreateSystemSparePartCommand request, CancellationToken ct)
    {
        var system = await SparePartCommandSupport.GetVisibleSystemAsync(_context, _scope, request.SystemInfoId, ct);
        if (system == null) return new(false, "Not found.", "NOT_FOUND");
        if (request.MinQty < 1) return new(false, "Định mức phải >= 1.", "INVALID_MIN_QTY");

        var (ok, name, error) = await SparePartCommandSupport.ValidateItemAsync(
            _context, system, request.ComponentId, request.ConsumableId, ct);
        if (!ok) return new(false, "Linh kiện/vật tư không tồn tại hoặc khác công ty.", error);

        var duplicate = await _context.SystemSparePartRequirements.AnyAsync(r =>
            r.SystemInfoId == request.SystemInfoId &&
            ((request.ComponentId.HasValue && r.ComponentId == request.ComponentId) ||
             (request.ConsumableId.HasValue && r.ConsumableId == request.ConsumableId)), ct);
        if (duplicate) return new(false, "Định mức này đã tồn tại.", "SPARE_REQUIREMENT_DUPLICATE");

        var entity = new SystemSparePartRequirement
        {
            CompanyId = system.CompanyId,
            SystemInfoId = request.SystemInfoId,
            ComponentId = request.ComponentId,
            ConsumableId = request.ConsumableId,
            MinQty = request.MinQty,
            Note = request.Note,
        };
        _context.SystemSparePartRequirements.Add(entity);
        await _context.SaveChangesAsync(ct);
        return new(true, "Đã thêm định mức.", Id: entity.Id, SystemInfoId: request.SystemInfoId,
            ItemName: name, MinQty: entity.MinQty, CompanyId: system.CompanyId);
    }
}

public class UpdateSystemSparePartCommandHandler
    : IRequestHandler<UpdateSystemSparePartCommand, SparePartCommandResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _scope;

    public UpdateSystemSparePartCommandHandler(IApplicationDbContext context, ICompanyScopeService scope)
    {
        _context = context;
        _scope = scope;
    }

    public async Task<SparePartCommandResult> Handle(UpdateSystemSparePartCommand request, CancellationToken ct)
    {
        var system = await SparePartCommandSupport.GetVisibleSystemAsync(_context, _scope, request.SystemInfoId, ct);
        if (system == null) return new(false, "Not found.", "NOT_FOUND");

        var entity = await _context.SystemSparePartRequirements.FirstOrDefaultAsync(
            r => r.Id == request.RequirementId && r.SystemInfoId == request.SystemInfoId, ct);
        if (entity == null) return new(false, "Not found.", "NOT_FOUND");
        if (request.MinQty is < 0) return new(false, "Định mức phải >= 0.", "INVALID_MIN_QTY");

        var changes = new ActionLogChanges()
            .Track("min_qty", entity.MinQty)
            .Track("note", entity.Note)
            .Track("is_active", entity.IsActive);

        if (request.MinQty.HasValue) entity.MinQty = request.MinQty.Value;
        if (request.Note != null) entity.Note = request.Note;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;

        changes.Set("min_qty", entity.MinQty).Set("note", entity.Note).Set("is_active", entity.IsActive);
        var name = entity.ComponentId.HasValue
            ? await _context.Components.AsNoTracking().Where(c => c.Id == entity.ComponentId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : await _context.Consumables.AsNoTracking().Where(c => c.Id == entity.ConsumableId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        await _context.SaveChangesAsync(ct);
        return new(true, "Đã cập nhật định mức.", Id: entity.Id, SystemInfoId: request.SystemInfoId,
            ItemName: name, MinQty: entity.MinQty, CompanyId: system.CompanyId, LogMeta: changes.ToLogMeta());
    }
}

public class DeleteSystemSparePartCommandHandler
    : IRequestHandler<DeleteSystemSparePartCommand, SparePartCommandResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _scope;

    public DeleteSystemSparePartCommandHandler(IApplicationDbContext context, ICompanyScopeService scope)
    {
        _context = context;
        _scope = scope;
    }

    public async Task<SparePartCommandResult> Handle(DeleteSystemSparePartCommand request, CancellationToken ct)
    {
        var system = await SparePartCommandSupport.GetVisibleSystemAsync(_context, _scope, request.SystemInfoId, ct);
        if (system == null) return new(false, "Not found.", "NOT_FOUND");

        var entity = await _context.SystemSparePartRequirements.FirstOrDefaultAsync(
            r => r.Id == request.RequirementId && r.SystemInfoId == request.SystemInfoId, ct);
        if (entity == null) return new(false, "Not found.", "NOT_FOUND");

        var name = entity.ComponentId.HasValue
            ? await _context.Components.AsNoTracking().Where(c => c.Id == entity.ComponentId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : await _context.Consumables.AsNoTracking().Where(c => c.Id == entity.ConsumableId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var meta = JsonSerializer.Serialize(new { item_name = name, min_qty = entity.MinQty });

        _context.SystemSparePartRequirements.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return new(true, "Đã xóa định mức.", Id: entity.Id, SystemInfoId: request.SystemInfoId,
            ItemName: name, CompanyId: system.CompanyId, LogMeta: meta);
    }
}
