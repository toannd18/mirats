using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace aspire_react.Server.Application.Common.Behaviors;

/// <summary>
/// Runs every <see cref="IValidator{TRequest}"/> registered for <typeparamref name="TRequest"/>
/// BEFORE the command handler executes. Previously validators were registered in DI but never
/// invoked in real request flows (no pipeline behavior existed), so DB-backed rules such as the
/// AssetTag uniqueness check only ran when unit tests called them manually.
/// On any validation failure a <see cref="ValidationException"/> is thrown, which is mapped to a
/// clean 400 response by <c>ValidationExceptionHandler</c> (instead of a raw 500 from the DB).
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (result.Errors is { Count: > 0 })
                failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
