using aspire_react.Server.Application.Accessories.Commands;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Common.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace aspire_react.Server.Application;

/// <summary>
/// Registers MediatR (CQRS) + FluentValidation + the ValidationBehavior pipeline (Task L). Extracted
/// from Program.cs (Task Q) — behavior unchanged. ValidationBehavior must be added here (AddOpenBehavior)
/// exactly where it was in the MediatR registration.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(CheckoutAssetCommand).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(CreateAccessoryCommand).Assembly);
            // [Giai đoạn 1.5] CacheInvalidationBehavior FIRST (outermost) — its post-phase runs LAST:
            // evict must happen AFTER ActionLogBehavior's log+commit, otherwise a concurrent GET could
            // re-cache the OLD data between evict and commit, and an eviction failure must not roll
            // back committed data. Effective sequence: Validation → Handler → Log+Commit → Evict.
            cfg.AddOpenBehavior(typeof(CacheInvalidationBehavior<,>));
            // Run FluentValidation validators on every command before its handler executes. Without this
            // pipeline behavior the registered validators never ran in real request flows (only in unit
            // tests that called them manually), e.g. the AssetTag uniqueness rule returned a raw 500 from
            // the DB unique index instead of a clean 400.
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            // [Giai đoạn 0.2 — M1] ActionLog for ILoggableCommand commands — registered AFTER
            // ValidationBehavior so it sits INNER: validation short-circuits invalid requests before
            // the transaction/log phase; the log is built+persisted only after the handler succeeded.
            // Opt-in via ILoggableCommand<TResponse> — non-marked commands pass through untouched.
            cfg.AddOpenBehavior(typeof(ActionLogBehavior<,>));
        });

        // Add FluentValidation
        services.AddValidatorsFromAssemblyContaining<CheckoutAssetCommandValidator>();

        return services;
    }
}
