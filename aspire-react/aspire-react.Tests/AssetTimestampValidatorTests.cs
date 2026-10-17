using System.Text.Json;
using aspire_react.Server.Application.Assets;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.ExceptionHandlers;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [Bước 0.2 / LL-3] Mốc thời gian ở tương lai quá 5 phút bị chặn ở tầng validator → HTTP 400
/// <c>TIMESTAMP_IN_FUTURE</c>. Mốc quá khứ (nhập bù) vẫn hợp lệ; archive không có tham số mốc thời gian.
/// </summary>
public class AssetTimestampValidatorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static CheckoutAssetCommand Checkout(DateTime? checkoutAt, Guid assetId, Guid userId) =>
        new(assetId, AssignmentTargetType.User, userId, null, checkoutAt, null, Guid.NewGuid());

    private static async Task<Guid> SeedPendingAssetAsync(AppDbContext db)
    {
        var asset = new Asset
        {
            AssetTag = $"TS-{Guid.NewGuid():N}"[..12],
            Name = "TS Asset",
            Status = AssetStatus.Pending,
            IsConfirmed = true
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    [Theory]
    [InlineData(10)]   // quá cửa sổ 5 phút
    [InlineData(6)]
    public async Task CheckoutValidator_Rejects_CheckoutAtInFuture(int minutesAhead)
    {
        await using var db = TestHelpers.CreateContext($"{nameof(CheckoutValidator_Rejects_CheckoutAtInFuture)}_{minutesAhead}");
        var assetId = await SeedPendingAssetAsync(db);
        var targetUser = new User { Username = "ts-target", FirstName = "TS", LastName = "User", Email = "ts@test.local", IsActive = true };
        db.Users.Add(targetUser);
        await db.SaveChangesAsync();

        var validator = new CheckoutAssetCommandValidator(db);
        var result = await validator.ValidateAsync(Checkout(Now.AddMinutes(minutesAhead), assetId, targetUser.Id));

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Errors);
        Assert.Equal(AssetTimestampRules.Code, failure.ErrorCode);
        Assert.Equal("CheckoutAt", failure.PropertyName);
    }

    [Theory]
    [InlineData(0)]    // bây giờ
    [InlineData(4)]    // trong cửa sổ cho phép
    [InlineData(5)]    // đúng biên: KHÔNG bị chặn (so sánh nghiêm ngặt)
    [InlineData(-60)]  // quá khứ (nhập bù) vẫn hợp lệ
    public async Task CheckoutValidator_Accepts_WhenWithinWindowOrPast(int minutesAhead)
    {
        await using var db = TestHelpers.CreateContext($"{nameof(CheckoutValidator_Accepts_WhenWithinWindowOrPast)}_{minutesAhead}");
        var assetId = await SeedPendingAssetAsync(db);
        var targetUser = new User { Username = "ts-target", FirstName = "TS", LastName = "User", Email = "ts@test.local", IsActive = true };
        db.Users.Add(targetUser);
        await db.SaveChangesAsync();

        var validator = new CheckoutAssetCommandValidator(db);
        var result = await validator.ValidateAsync(Checkout(Now.AddMinutes(minutesAhead), assetId, targetUser.Id));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.PropertyName}:{e.ErrorMessage}")));
    }

    [Fact]
    public void CheckinValidator_Rejects_CheckinAtInFuture_AndAccepts_PastOrNull()
    {
        var validator = new CheckinAssetCommandValidator();
        var id = Guid.NewGuid();

        var tooFar = validator.Validate(new CheckinAssetCommand(id, id, null, Now.AddMinutes(30), Guid.NewGuid()));
        Assert.False(tooFar.IsValid);
        var failure = Assert.Single(tooFar.Errors);
        Assert.Equal(AssetTimestampRules.Code, failure.ErrorCode);
        Assert.Equal("CheckinAt", failure.PropertyName);

        Assert.True(validator.Validate(new CheckinAssetCommand(id, id, null, null, Guid.NewGuid())).IsValid);
        Assert.True(validator.Validate(new CheckinAssetCommand(id, id, null, Now.AddMinutes(5), Guid.NewGuid())).IsValid);
        Assert.True(validator.Validate(new CheckinAssetCommand(id, id, null, Now.AddMinutes(-120), Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void Rules_UseTheSharedFiveMinuteWindow_StrictBoundary()
    {
        Assert.Equal(5, AssetTimestampRules.MaxFutureMinutes);
        Assert.False(AssetTimestampRules.IsBeyondFutureWindow(null, Now));
        Assert.False(AssetTimestampRules.IsBeyondFutureWindow(Now.AddMinutes(5), Now));
        Assert.True(AssetTimestampRules.IsBeyondFutureWindow(Now.AddMinutes(5).AddSeconds(1), Now));
    }

    /// <summary>
    /// Handler map lỗi validation: error code do dự án tự đặt được phơi ra ở <c>error_code</c> (HTTP 400),
    /// còn error code mặc định của FluentValidation (PascalCase) thì KHÔNG.
    /// </summary>
    [Fact]
    public async Task ValidationExceptionHandler_ExposesProjectErrorCode_Only()
    {
        var project = await HandleAsync(new ValidationFailure("CheckoutAt", "future")
        {
            ErrorCode = AssetTimestampRules.Code
        });
        Assert.Equal(400, project.Status);
        Assert.Equal(AssetTimestampRules.Code, project.ErrorCode);
        Assert.Contains("CheckoutAt", project.Errors.Keys);

        var builtIn = await HandleAsync(new ValidationFailure("Name", "required")
        {
            ErrorCode = "NotNullValidator"
        });
        Assert.Equal(400, builtIn.Status);
        Assert.Null(builtIn.ErrorCode);
    }

    [Fact]
    public async Task ValidationExceptionHandler_ExposesSingleProjectCode_EvenWithBuiltInCodesAlongside()
    {
        var mixed = await HandleAsync(
            new ValidationFailure("CheckoutAt", "future") { ErrorCode = AssetTimestampRules.Code },
            new ValidationFailure("TargetId", "empty") { ErrorCode = "NotEmptyValidator" });
        Assert.Equal(400, mixed.Status);
        Assert.Equal(AssetTimestampRules.Code, mixed.ErrorCode);
        Assert.Contains("TargetId", mixed.Errors.Keys);
    }

    [Fact]
    public async Task ValidationExceptionHandler_OmitsErrorCode_WhenTwoProjectCodesDisagree()
    {
        var ambiguous = await HandleAsync(
            new ValidationFailure("CheckoutAt", "future") { ErrorCode = AssetTimestampRules.Code },
            new ValidationFailure("LocationId", "missing") { ErrorCode = "LOCATION_REQUIRED" });
        Assert.Equal(400, ambiguous.Status);
        Assert.Null(ambiguous.ErrorCode);
    }

    private static async Task<(int Status, string? ErrorCode, Dictionary<string, string[]> Errors)> HandleAsync(
        params ValidationFailure[] failures)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handled = await new ValidationExceptionHandler().TryHandleAsync(
            context, new ValidationException(failures), CancellationToken.None);
        Assert.True(handled);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        var root = doc.RootElement;
        var errors = root.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
        var errorCode = root.TryGetProperty("error_code", out var code) ? code.GetString() : null;
        return (context.Response.StatusCode, errorCode, errors);
    }
}
