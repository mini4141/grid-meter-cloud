using FluentValidation;
using GridMeter.Application.Abstractions;
using GridMeter.Domain.DemandResponse;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GridMeter.Application.DemandResponse;

public sealed record IssueDemandResponseCommand(Guid SiteId, DateTime WindowStart, DateTime WindowEnd, decimal TargetKw) : ICommand<Guid>;

public sealed class IssueDemandResponseValidator : AbstractValidator<IssueDemandResponseCommand>
{
    public IssueDemandResponseValidator()
    {
        RuleFor(x => x.WindowEnd).GreaterThan(x => x.WindowStart);
        RuleFor(x => x.TargetKw).GreaterThan(0);
    }
}

public sealed class IssueDemandResponseHandler(IAppDbContext db) : IRequestHandler<IssueDemandResponseCommand, Guid>
{
    public async Task<Guid> Handle(IssueDemandResponseCommand request, CancellationToken ct)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == request.SiteId, ct)) throw new KeyNotFoundException("Site not found");
        var e = DemandResponseEvent.Issue(request.SiteId, request.WindowStart, request.WindowEnd, request.TargetKw);
        db.DemandResponseEvents.Add(e);
        return e.Id;
    }
}
