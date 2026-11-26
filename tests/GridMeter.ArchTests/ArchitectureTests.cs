using NetArchTest.Rules;
using Shouldly;

namespace GridMeter.ArchTests;

public class ArchitectureTests
{
    private const string Domain = "GridMeter.Domain";
    private const string Application = "GridMeter.Application";
    private const string Infrastructure = "GridMeter.Infrastructure";
    private const string Api = "GridMeter.Api";

    private static readonly System.Reflection.Assembly DomainAsm = typeof(GridMeter.Domain.Common.Entity).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAsm = typeof(GridMeter.Application.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAsm = typeof(GridMeter.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else_in_the_solution()
    {
        var result = Types.InAssembly(DomainAsm).ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Api, "MediatR", "Microsoft.EntityFrameworkCore").GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        var result = Types.InAssembly(ApplicationAsm).ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Api).GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Api()
    {
        var result = Types.InAssembly(InfrastructureAsm).ShouldNot().HaveDependencyOn(Api).GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }
}
