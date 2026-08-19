using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// ST6a â€” Delete guard cho CustomField: field Ä‘ang Ä‘Æ°á»£c CustomFieldFieldset tham chiáº¿u
/// (FK FieldId â†’ CustomField, OnDelete(Cascade)) khÃ´ng Ä‘Æ°á»£c xÃ³a â€” náº¿u khÃ´ng, pivot rows
/// fieldâ†”fieldset sáº½ bá»‹ cascade xÃ³a sáº¡ch (bug class F7). Field khÃ´ng liÃªn káº¿t xÃ³a bÃ¬nh thÆ°á»ng.
/// </summary>
public class CustomFieldDeleteGuardTests
{
    /// <summary>Superuser scope so the FMCS global query filters short-circuit (see-all).</summary>
    private sealed class SuperUserScope : ICompanyScopeService
    {
        public bool IsSuperUser() => true;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(null);
    }

    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options, new SuperUserScope());
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext ctx)
    {
        var user = new User
        {
            Username = "cf-admin",
            Email = "cf-admin@test.local",
            FirstName = "Nguyá»…n",
            LastName = "VÄƒn A"
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<CustomFieldsController> CreateControllerAsync(AppDbContext ctx, Guid localUserId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("local_user_id", localUserId.ToString())
            }, "Test"))
        };
        var controller = new CustomFieldsController(ctx, TestHelpers.CreateActionLogService(ctx))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        return controller;
    }

    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string ReadErrorCode(object? value)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value, WebJson));
        return doc.RootElement.GetProperty("error_code").GetString()!;
    }

    [Fact]
    public async Task Delete_FieldLinkedToFieldset_Blocked_DataIntact_NoDeleteLog()
    {
        await using var ctx = CreateContext(nameof(Delete_FieldLinkedToFieldset_Blocked_DataIntact_NoDeleteLog));
        var adminId = await SeedUserAsync(ctx);

        var field = new CustomField { Name = "MÃ£ mÃ u", Slug = "mau_sac", Format = "TEXT" };
        var fieldset = new CustomFieldset { Name = "Fieldset MÃ¡y in" };
        ctx.CustomFields.Add(field);
        ctx.CustomFieldsets.Add(fieldset);
        await ctx.SaveChangesAsync();
        ctx.CustomFieldFieldsets.Add(new CustomFieldFieldset
        {
            FieldsetId = fieldset.Id,
            FieldId = field.Id,
            Required = true,
            Order = 1
        });
        await ctx.SaveChangesAsync();

        var controller = await CreateControllerAsync(ctx, adminId);

        var result = await controller.Delete(field.Id);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CUSTOM_FIELD_IN_USE", ReadErrorCode(bad.Value));
        // Data intact â€” field + pivot row both survive.
        Assert.True(await ctx.CustomFields.AnyAsync(f => f.Id == field.Id));
        Assert.True(await ctx.CustomFieldFieldsets.AnyAsync(cf => cf.FieldId == field.Id));
        // No Delete ActionLog was written.
        Assert.Empty(await ctx.ActionLogs.Where(l => l.ItemType == ItemType.CustomField && l.ActionType == ActionType.Delete).ToListAsync());
    }

    [Fact]
    public async Task Delete_FieldNotLinked_RemovesAndLogsDelete()
    {
        await using var ctx = CreateContext(nameof(Delete_FieldNotLinked_RemovesAndLogsDelete));
        var adminId = await SeedUserAsync(ctx);

        var field = new CustomField { Name = "Sá»‘ series", Slug = "serial", Format = "TEXT" };
        ctx.CustomFields.Add(field);
        await ctx.SaveChangesAsync();

        var controller = await CreateControllerAsync(ctx, adminId);

        var result = await controller.Delete(field.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await ctx.CustomFields.AnyAsync(f => f.Id == field.Id));
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.CustomField && l.ActionType == ActionType.Delete);
        Assert.Equal(adminId, log.CreatedBy);
        Assert.Equal(field.Id, log.ItemId);
    }
}
