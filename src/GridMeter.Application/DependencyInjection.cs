using FluentValidation;
using GridMeter.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace GridMeter.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMediatR(c =>
        {
            c.RegisterServicesFromAssembly(assembly);
            c.AddOpenBehavior(typeof(ValidationBehavior<,>));
            c.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);
        return services;
    }
}
