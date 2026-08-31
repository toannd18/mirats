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

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(255).WithMessage("Email must not exceed 255 characters.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MustAsync(BeUniqueEmailForUpdate).WithMessage("Email already in use by another user.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.EmployeeNumber)
            .MaximumLength(50).WithMessage("Employee number must not exceed 50 characters.");

        RuleFor(x => x.JobTitle)
            .MaximumLength(200).WithMessage("Job title must not exceed 200 characters.");
    }

    private async Task<bool> BeUniqueEmailForUpdate(
        UpdateUserCommand command,
        string email,
        CancellationToken ct)
    {
        var trimmed = email.Trim().ToLowerInvariant();
        return !await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == trimmed && u.Id != command.Id, ct);
    }
}