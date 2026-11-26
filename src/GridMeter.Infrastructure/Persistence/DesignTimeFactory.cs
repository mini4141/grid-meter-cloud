using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GridMeter.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef migrations`.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=GridMeter;User Id=sa;Password=design-time;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(new AsyncLocalMultiTenantContextAccessor<TenantInfo>(), options);
    }
}
