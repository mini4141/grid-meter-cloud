using System.Text.Json;
using FluentValidation;
using GridMeter.Application.Abstractions;
using GridMeter.Application.Messaging;
using GridMeter.Domain.Common;
using MediatR;

namespace GridMeter.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var failures = validators
            .Select(v => v.Validate(request))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();
        if (failures.Count > 0) throw new ValidationException(failures);
        return await next();
    }
}

public sealed class TransactionBehavior<TRequest, TResponse>(IAppDbContext db, ICurrentTenant tenant)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var response = await next();

        var aggregates = db.ChangeTracker.Entries<Entity>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();
        foreach (var aggregate in aggregates)
        {
            foreach (var e in aggregate.DomainEvents)
            {
                db.OutboxMessages.Add(new OutboxMessage
                {
                    TenantId = tenant.Id ?? "",
                    Type = e.GetType().Name,
                    Payload = JsonSerializer.Serialize(e, e.GetType())
                });
            }
            aggregate.ClearEvents();
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return response;
    }
}
