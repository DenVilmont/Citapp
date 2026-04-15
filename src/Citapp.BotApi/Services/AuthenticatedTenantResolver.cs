using System.Security.Claims;
using Citapp.BotApi.Infrastructure.Repositories;

namespace Citapp.BotApi.Services;

public class AuthenticatedTenantResolver
{
    private readonly TenantRepository _tenantRepository;

    public AuthenticatedTenantResolver(TenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<Guid?> ResolveTenantIdAsync(ClaimsPrincipal user)
    {
        var rawUserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(rawUserId, out var ownerUserId))
        {
            return null;
        }

        return await _tenantRepository.GetTenantIdByOwnerAsync(ownerUserId);
    }
}
