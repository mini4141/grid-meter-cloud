using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using GridMeter.Application.Abstractions;

namespace GridMeter.Infrastructure.Tenancy;

public sealed class CurrentTenant(IMultiTenantContextAccessor accessor) : ICurrentTenant
{
    public string? Id => accessor.MultiTenantContext?.TenantInfo?.Id;
}

public sealed class TenantSwitcher(IMultiTenantContextSetter setter) : ITenantSwitcher
{
    public void Use(string tenantId) =>
        setter.MultiTenantContext = new MultiTenantContext<TenantInfo>(new TenantInfo { Id = tenantId, Identifier = tenantId });
}
