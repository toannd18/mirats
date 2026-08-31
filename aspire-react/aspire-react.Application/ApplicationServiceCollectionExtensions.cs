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
            // Run FluentValidation validators on every command before its handler executes. Without this
            // pipeline behavior the registered validators never ran in real request flows (only in unit
            // tests that called them manually), e.g. the AssetTag uniqueness rule returned a raw 500 from
            // the DB unique index instead of a clean 400.
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // Add FluentValidation
        services.AddValidatorsFromAssemblyContaining<CheckoutAssetCommandValidator>();

        return services;
    }
}
