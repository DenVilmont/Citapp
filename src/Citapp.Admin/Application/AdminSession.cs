using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;
using Supabase;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Citapp.Admin.Application;

public class AdminSession
{
    private readonly Client _supabase;
    private readonly ITenantRepository _tenantRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly string _supabaseUrl;
    private readonly string _supabaseAnonKey;

    public AdminSession(Client supabase, ITenantRepository tenantRepository, IProfileRepository profileRepository, IConfiguration configuration)
    {
        _supabase = supabase;
        _tenantRepository = tenantRepository;
        _profileRepository = profileRepository;
        _supabaseUrl = (configuration["Supabase:Url"] ?? string.Empty).TrimEnd('/');
        _supabaseAnonKey = configuration["Supabase:AnonKey"] ?? string.Empty;
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

    public async Task<bool> SendMagicLinkAsync(string email)
    {
        await _supabase.InitializeAsync();

        if (string.IsNullOrWhiteSpace(_supabaseUrl) || string.IsNullOrWhiteSpace(_supabaseAnonKey))
        {
            return false;
        }

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/auth/v1/otp");
        request.Headers.Add("apikey", _supabaseAnonKey);

        request.Content = JsonContent.Create(new MagicLinkRequest(
            email.Trim(),
            false,
            null));

        var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
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

    private sealed record MagicLinkRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("create_user")] bool CreateUser,
        [property: JsonPropertyName("email_redirect_to")] string? EmailRedirectTo);
}
