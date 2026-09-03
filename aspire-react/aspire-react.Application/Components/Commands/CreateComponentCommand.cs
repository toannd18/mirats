using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Components.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] POST /api/v1/components (extracted from ComponentsController.Create).
/// ⚠️ TRANSACTION-BOUNDARY-CRITICAL: the handler owns the Npgsql execution strategy + explicit
/// transaction (moved VERBATIM from the controller) — component + optional initial StockIn + ActionLog
/// commit/rollback TOGETHER. The allocation service relies on being called INSIDE this transaction
/// (its FOR UPDATE row lock + SaveChanges commit within the caller's boundary).
/// Validation verbatim: Serial→no-Qty / Bulk→Qty>0 / CATEGORY_REQUIRED / INVALID_CATEGORY /
/// COMPANY_REQUIRED / INVALID_COMPANY. Log written via IActionLogService INSIDE the transaction
/// (same SaveChanges as data — NOT ILoggableCommand, whose behavior-log would fall outside the tx).
/// </summary>
public record CreateComponentCommand(
    string Name, string? Serial, int? Qty, int MinAmt, Guid? CategoryId, Guid? LocationId,
    Guid? CompanyId, Guid? SupplierId, Guid? ManufacturerId, string? ModelNumber,
    string? OrderNumber, decimal? PurchaseCost, DateTime? PurchaseDate, string? Notes,
    TrackingType TrackingType, List<string>? SerialNumbers, Guid CurrentUserId)
    : IRequest<CreateComponentResult>;

public record CreateComponentResult(
    bool Success, string Message, string? ErrorCode = null,
    Guid? Id = null, string? Name = null, int? Qty = null, string? TrackingType = null);

public class CreateComponentCommandHandler : IRequestHandler<CreateComponentCommand, CreateComponentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IComponentAllocationService _allocationService;
    private readonly IActionLogService _actionLogService;

    public CreateComponentCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IComponentAllocationService allocationService, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _allocationService = allocationService;
        _actionLogService = actionLogService;
    }

    public async Task<CreateComponentResult> Handle(CreateComponentCommand request, CancellationToken cancellationToken)
    {
        if (request.TrackingType == TrackingType.Serial && request.Qty.HasValue)
            return new CreateComponentResult(false, "Không gửi qty khi tạo linh kiện Serial — số lượng được suy ra từ danh sách serial.");
        if (request.TrackingType == TrackingType.Bulk && (!request.Qty.HasValue || request.Qty.Value <= 0))
            return new CreateComponentResult(false, "Qty bắt buộc (>0) cho linh kiện Bulk.");
        if (!request.CategoryId.HasValue)
            return new CreateComponentResult(false, "Danh mục (Category) là bắt buộc khi tạo linh kiện.", "CATEGORY_REQUIRED");
        var categoryExists = await _context.Categories.AnyAsync(c => c.Id == request.CategoryId.Value && c.CategoryType == CategoryType.Component, cancellationToken);
        if (!categoryExists)
            return new CreateComponentResult(false, "Danh mục không hợp lệ (phải thuộc loại Component).", "INVALID_CATEGORY");
        if (!request.CompanyId.HasValue)
            return new CreateComponentResult(false, "Công ty (Company) là bắt buộc khi tạo linh kiện.", "COMPANY_REQUIRED");
        if (!await _context.Companies.AnyAsync(c => c.Id == request.CompanyId.Value, cancellationToken))
            return new CreateComponentResult(false, "Công ty không hợp lệ.", "INVALID_COMPANY");

        // [Task L2] Company-scoping on CREATE: a regular user may only create components for their
        // own company (or company-less floater). Superuser (scope → null) may create for any company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
            return new CreateComponentResult(false, "Bạn chỉ được tạo linh kiện cho công ty của mình.", "COMPANY_MISMATCH");

        var userId = request.CurrentUserId;

        // Npgsql retrying execution strategy requires transactions to run inside CreateExecutionStrategy.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<CreateComponentResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            var component = new Component
            {
                Name = request.Name,
                Serial = request.Serial,
                Qty = 0,
                MinAmt = request.MinAmt,
                TrackingType = request.TrackingType,
                CategoryId = request.CategoryId,
                LocationId = request.LocationId,
                CompanyId = request.CompanyId,
                SupplierId = request.SupplierId,
                ManufacturerId = request.ManufacturerId,
                ModelNumber = request.ModelNumber,
                OrderNumber = request.OrderNumber,
                PurchaseDate = request.PurchaseDate,
                PurchaseCost = request.PurchaseCost,
                Notes = request.Notes
            };
            _context.Components.Add(component);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Component,
                ItemId = component.Id,
                ActionType = ActionType.Create,
                CreatedBy = userId,
                CompanyId = component.CompanyId,
                Note = $"Tạo linh kiện (TrackingType: {request.TrackingType})"
            });

            if (request.TrackingType == TrackingType.Bulk)
            {
                component.Qty = request.Qty!.Value;
            }
            else if (request.SerialNumbers?.Any() == true)
            {
                // Initial serial stock — reuse the StockIn path so serial validation + per-unit audit logging
                // stay in one place. Component + units + logs all commit in the same transaction.
                await _context.SaveChangesAsync(cancellationToken);
                var result = await _allocationService.StockInAsync(component.Id, request.SerialNumbers, "Nhập kho ban đầu khi tạo", userId, cancellationToken);
                if (!result.Success)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return new CreateComponentResult(false, result.Message, result.ErrorCode);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new CreateComponentResult(true, "Component created.",
                Id: component.Id, Name: component.Name, Qty: component.Qty, TrackingType: component.TrackingType.ToString());
        });
    }
}
