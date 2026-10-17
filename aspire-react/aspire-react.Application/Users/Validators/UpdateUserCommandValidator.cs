using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Application.Common.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Users.Validators;

/// <summary>
/// Validator for UpdateUserCommand. Validates input format.
/// Email uniqueness check excludes the current user being updated.
/// </summary>
public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdateUserCommandValidator(IApplicationDbContext context)
    {
        _context = context;

        // [FIX BUG-O] Patch semantics: 3 field bắt buộc chỉ được VALIDATE KHI ĐƯỢC GỬI
        // (null = không gửi ⇒ giữ nguyên giá trị cũ, không cần validate). Gửi chuỗi rỗng "" vẫn bị
        // chặn bởi NotEmpty ⇒ 400 — field bắt buộc không thể bị xoá.
        When(x => x.Email is not null, () =>
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .MaximumLength(255).WithMessage("Email must not exceed 255 characters.")
                .EmailAddress().WithMessage("Email must be a valid email address.")
                .MustAsync(BeUniqueEmailForUpdate).WithMessage("Email already in use by another user.");
        });

        When(x => x.FirstName is not null, () =>
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("First name is required.")
                .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");
        });

        When(x => x.LastName is not null, () =>
        {
            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("Last name is required.")
                .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");
        });

        RuleFor(x => x.EmployeeNumber)
            .MaximumLength(50).WithMessage("Employee number must not exceed 50 characters.");

        RuleFor(x => x.JobTitle)
            .MaximumLength(200).WithMessage("Job title must not exceed 200 characters.");
    }

    private async Task<bool> BeUniqueEmailForUpdate(
        UpdateUserCommand command,
        string? email,
        CancellationToken ct)
    {
        // [FIX BUG-O] Rule này chỉ chạy khi email ĐƯỢC GỬI; guard cho an toàn (rỗng đã bị NotEmpty chặn).
        if (string.IsNullOrWhiteSpace(email))
            return true;

        var trimmed = email.Trim().ToLowerInvariant();
        return !await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == trimmed && u.Id != command.Id, ct);
    }
}