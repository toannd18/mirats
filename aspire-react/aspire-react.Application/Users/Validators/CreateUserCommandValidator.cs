using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Application.Common.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Users.Validators;

/// <summary>
/// Validator for CreateUserCommand. Validates input format and database uniqueness.
/// </summary>
public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    private readonly IApplicationDbContext _context;

    public CreateUserCommandValidator(IApplicationDbContext context)
    {
        _context = context;

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(100).WithMessage("Username must not exceed 100 characters.")
            .Matches(@"^\S+$").WithMessage("Username must not contain spaces.")
            .MustAsync(BeUniqueUsername).WithMessage("Username already exists.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(255).WithMessage("Email must not exceed 255 characters.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MustAsync(BeUniqueEmail).WithMessage("Email already exists.");

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

    private async Task<bool> BeUniqueUsername(string username, CancellationToken ct)
    {
        var trimmed = username.Trim();
        return !await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Username == trimmed, ct);
    }

    private async Task<bool> BeUniqueEmail(string email, CancellationToken ct)
    {
        var trimmed = email.Trim().ToLowerInvariant();
        return !await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == trimmed, ct);
    }
}