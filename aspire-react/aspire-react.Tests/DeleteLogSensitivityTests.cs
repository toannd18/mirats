using System.Text.Json;
using aspire_react.Server.Application.Auth.Commands;
using aspire_react.Server.Application.Companies.Commands;
using aspire_react.Server.Application.Licenses.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B6b] Xóa cứng phải để lại **ảnh chụp tối thiểu** (id + mã + tên) và **TUYỆT ĐỐI KHÔNG**
/// đưa dữ liệu nhạy cảm vào nhật ký: `User.PasswordHash`, `License.Serial`, passkey
/// `CredentialId`/`PublicKey`.
///
/// <para>
/// Gồm 2 lớp: (1) **guard tĩnh** quét mọi file `Delete*.cs` (chặn hồi quy khi thêm field nhạy cảm vào
/// log delete); (2) **test hành vi** trên các ca nhạy cảm nhất (license serial, passkey credential/public
/// key, company snapshot) — chạy handler thật rồi soi `action_logs`.
/// </para>
/// </summary>
public class DeleteLogSensitivityTests
{
    private static readonly string[] ForbiddenInDeleteLogs =
    {
        "PasswordHash", "password_hash", "passwordHash",
        "PublicKey", "public_key", "publicKey",
        "CredentialId", "credential_id", "credentialId"
    };

    // ==================== 1. Guard tĩnh ====================

    [Fact]
    public void DeleteCommandSources_NeverReferenceSensitiveFields()
    {
        var root = FindApplicationSourceRoot();
        var deleteFiles = Directory.GetFiles(root, "Delete*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.NotEmpty(deleteFiles);
        foreach (var file in deleteFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenInDeleteLogs)
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} chứa '{token}' — Delete không được ghi dữ liệu nhạy cảm vào log.");
            }
        }

        // License serial: file xóa license không được tham chiếu tới Serial (nhạy cảm).
        var licenseDelete = deleteFiles.Single(f => f.EndsWith("DeleteLicenseCommand.cs", StringComparison.Ordinal));
        Assert.DoesNotContain("Serial", File.ReadAllText(licenseDelete));
    }

    // ==================== 2. Test hành vi ====================

    [Fact]
    public async Task DeleteLicense_LogsNoSerial()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(DeleteLicense_LogsNoSerial));
        var actor = Guid.NewGuid();
        var license = new License { Name = "License QA", Serial = "SN-SECRET-911", Seats = 1, Reassignable = true };
        ctx.Licenses.Add(license);
        await ctx.SaveChangesAsync();

        var result = await new DeleteLicenseCommandHandler(ctx, new TestHelpers.SuperUserScope(),
                TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new DeleteLicenseCommand(license.Id, actor), CancellationToken.None);

        Assert.True(result.Success, result.ErrorCode);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.License && l.ActionType == ActionType.Delete);
        Assert.Null(log.LogMeta);                       // soft-delete ⇒ không cần snapshot, càng không lộ serial
        Assert.DoesNotContain("SN-SECRET-911", log.LogMeta ?? string.Empty);
    }

    [Fact]
    public async Task DeletePasskey_LogsNameOnly_NoCredentialIdOrPublicKey()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(DeletePasskey_LogsNameOnly_NoCredentialIdOrPublicKey));
        var user = new User { Username = "pk-user", Email = "pk@test.local", FirstName = "P", LastName = "K", IsActive = true };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var actor = user.Id; // passkey chỉ xóa được bởi CHÍNH chủ sở hữu (handler lọc UserId == CurrentUserId)
        var passkey = new UserPasskey
        {
            UserId = user.Id,
            CredentialId = "CRED-SECRET-123",
            PublicKey = "PUB-SECRET-456",
            SignCount = 3,
            Name = "YubiKey QA"
        };
        ctx.UserPasskeys.Add(passkey);
        await ctx.SaveChangesAsync();

        var result = await TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), actor)
            .Send(new DeletePasskeyCommand(actor, passkey.Id));

        Assert.True(result.Success, result.ErrorCode);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.User && l.ActionType == ActionType.Delete);
        Assert.NotNull(log.LogMeta);
        Assert.DoesNotContain("CRED-SECRET-123", log.LogMeta!);
        Assert.DoesNotContain("PUB-SECRET-456", log.LogMeta!);

        using var doc = JsonDocument.Parse(log.LogMeta!);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "id", "name" }, keys); // tối thiểu: id + tên gợi nhớ
    }

    [Fact]
    public async Task DeleteCompany_LogsMinimalSnapshot_NotSensitive()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(DeleteCompany_LogsMinimalSnapshot_NotSensitive));
        var actor = Guid.NewGuid();
        var company = new Company { Code = "B6B", Name = "Công ty B6B" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();

        var result = await TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), actor)
            .Send(new DeleteCompanyCommand(company.Id, actor));

        Assert.True(result.Success, result.ErrorCode);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Company && l.ActionType == ActionType.Delete);
        Assert.NotNull(log.LogMeta);

        using var doc = JsonDocument.Parse(log.LogMeta!);
        Assert.Equal(company.Id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("B6B", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Công ty B6B", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(3, doc.RootElement.EnumerateObject().Count());
    }

    // ==================== hạ tầng ====================

    private static string FindApplicationSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "aspire-react.Application");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Không tìm thấy thư mục source 'aspire-react.Application' từ " + AppContext.BaseDirectory);
    }
}
