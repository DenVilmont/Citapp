using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;
using Supabase;

namespace Citapp.Admin.Application;

public class AdminSession
{
    private readonly Client _supabase;
    private readonly ITenantRepository _tenantRepository;
    private readonly IProfileRepository _profileRepository;

    public AdminSession(Client supabase, ITenantRepository tenantRepository, IProfileRepository profileRepository)
    {
        _supabase = supabase;
        _tenantRepository = tenantRepository;
        _profileRepository = profileRepository;
    }

    public Guid? UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public bool IsAuthenticated => UserId is not null;
    public string AccessToken => _supabase.Auth.CurrentSession?.AccessToken ?? string.Empty;

    public async Task InitializeAsync()
    {
        await _supabase.InitializeAsync();
        var user = _supabase.Auth.CurrentUser;
        if (user is null)
        {
            Clear();
            return;
        }

        if (!Guid.TryParse(user.Id, out var userId))
        {
            Clear();
            return;
        }

        UserId = userId;
        Email = user.Email ?? string.Empty;
        var tenant = await _tenantRepository.GetByOwnerAsync(userId);
        TenantId = tenant?.Id;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        await _supabase.InitializeAsync();
        var session = await _supabase.Auth.SignIn(email, password);
        if (session?.User is null || !Guid.TryParse(session.User.Id, out var userId))
        {
            return false;
        }

        UserId = userId;
        Email = session.User.Email ?? email;
        var tenant = await _tenantRepository.GetByOwnerAsync(userId);
        TenantId = tenant?.Id;
        return true;
    }

    public async Task<bool> RegisterAsync(string email, string password, string? displayName)
    {
        await _supabase.InitializeAsync();
        var session = await _supabase.Auth.SignUp(email, password);
        var user = session?.User;
        if (user is null || !Guid.TryParse(user.Id, out var userId))
        {
            return false;
        }

        await _profileRepository.UpsertAsync(new ProfileDto(userId, email, displayName, DateTimeOffset.UtcNow));
        var tenant = await _tenantRepository.UpsertDefaultTenantAsync(userId, email);

        UserId = userId;
        Email = email;
        TenantId = tenant.Id;
        return true;
    }

    public async Task LogoutAsync()
    {
        await _supabase.Auth.SignOut();
        Clear();
    }

    private void Clear()
    {
        UserId = null;
        Email = string.Empty;
        TenantId = null;
    }
}
