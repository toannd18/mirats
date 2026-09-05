using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Models.Commands;

/// <summary>
/// [BUG-H FIX 2026-09-05] Shared validation for Model Create/Update (behavior change approved):
///   1. Empty-name check — "Tên model không được để trống." (400, no error_code — soft-fail
///      section style, mirrors CreateDepartmentCommand).
///   2. Duplicate-name check (Create: exact; Update: only when the name actually CHANGES,
///      excluding self) — "Tên model đã tồn tại." (400).
///   3. FK existence for ManufacturerId/CategoryId/DepreciationId/FieldsetId — any NON-NULL
///      supplied id that does not exist → 400 "RESOURCE_NOT_FOUND" (previously a raw FK-violation
///      500 at SaveChanges). Checked via ONE COUNT query per supplied FK, BEFORE any mutation.
/// Messages/error-code style matches Manufacturer/Supplier dup-checks and the section's
/// soft-fail convention (no FluentValidation envelope).
/// </summary>
internal static class ModelValidation
{
    internal static async Task<string?> ValidateNameAsync(
        IApplicationDbContext context, string? name, Guid? excludeModelId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Tên model không được để trống.";
        var dup = await context.Models.AsNoTracking()
            .AnyAsync(x => x.Name == name && (excludeModelId == null || x.Id != excludeModelId.Value), ct);
        if (dup)
            return "Tên model đã tồn tại.";
        return null;
    }

    /// <summary>Validates the 4 optional FK fields exist. Returns the failing field name, or null.</summary>
    internal static async Task<string?> ValidateForeignKeysAsync(
        IApplicationDbContext context, Guid? manufacturerId, Guid? categoryId,
        Guid? depreciationId, Guid? fieldsetId, CancellationToken ct)
    {
        if (manufacturerId.HasValue && !await context.Manufacturers.AnyAsync(x => x.Id == manufacturerId.Value, ct))
            return "manufacturerId";
        if (categoryId.HasValue && !await context.Categories.AnyAsync(x => x.Id == categoryId.Value, ct))
            return "categoryId";
        if (depreciationId.HasValue && !await context.Depreciations.AnyAsync(x => x.Id == depreciationId.Value, ct))
            return "depreciationId";
        if (fieldsetId.HasValue && !await context.CustomFieldsets.AnyAsync(x => x.Id == fieldsetId.Value, ct))
            return "fieldsetId";
        return null;
    }

    internal static ModelResult FkNotFound(string field)
        => new(false, $"Trường tham chiếu không tồn tại: {field}.", "RESOURCE_NOT_FOUND");
}
