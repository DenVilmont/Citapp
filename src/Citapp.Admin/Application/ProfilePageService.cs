using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;

namespace Citapp.Admin.Application;

public sealed class ProfilePageService
{
    private readonly IProfileRepository _profileRepository;
    private readonly ITenantRepository _tenantRepository;

    public ProfilePageService(IProfileRepository profileRepository, ITenantRepository tenantRepository)
    {
        _profileRepository = profileRepository;
        _tenantRepository = tenantRepository;
    }

    public async Task<ProfilePageState> LoadAsync(Guid userId, Guid tenantId, string fallbackEmail)
    {
        var profile = await _profileRepository.GetAsync(userId);
        var tenant = await _tenantRepository.GetAsync(tenantId);

        return new ProfilePageState(
            profile?.Email ?? fallbackEmail,
            profile?.DisplayName ?? string.Empty,
            tenant?.BusinessName ?? string.Empty,
            tenant?.AboutText ?? string.Empty,
            tenant?.GreetingText ?? string.Empty,
            tenant?.Timezone ?? "Europe/Madrid",
            tenant?.Currency ?? "EUR",
            tenant?.SlotStepMinutes ?? 15,
            tenant?.DefaultBufferMinutes ?? 0,
            tenant?.BookingEnabled ?? true);
    }

    public async Task<string?> SaveAsync(Guid userId, Guid tenantId, ProfilePageState state)
    {
        var validationError = Validate(state);
        if (validationError is not null)
        {
            return validationError;
        }

        await _profileRepository.UpsertAsync(new ProfileDto(userId, state.Email, state.DisplayName, DateTimeOffset.UtcNow));

        var existing = await _tenantRepository.GetAsync(tenantId);
        if (existing is not null)
        {
            await _tenantRepository.UpdateAsync(existing.Id, existing with
            {
                BusinessName = state.BusinessName,
                AboutText = state.AboutText,
                GreetingText = state.GreetingText,
                Timezone = state.Timezone,
                Currency = state.Currency.Trim().ToUpperInvariant(),
                SlotStepMinutes = state.SlotStep,
                DefaultBufferMinutes = state.Buffer,
                BookingEnabled = state.BookingEnabled
            });
        }

        return null;
    }

    private static string? Validate(ProfilePageState state)
    {
        if (string.IsNullOrWhiteSpace(state.BusinessName)) return "Business name is required.";
        if (string.IsNullOrWhiteSpace(state.GreetingText)) return "Greeting text is required.";
        if (!IsValidCurrency(state.Currency)) return "Use a valid 3-letter currency code.";
        if (!IsValidTimeZone(state.Timezone)) return "Timezone is invalid.";
        if (state.SlotStep <= 0 || state.SlotStep > 120) return "Slot step must be between 1 and 120.";
        if (state.Buffer < 0 || state.Buffer > 240) return "Buffer must be between 0 and 240.";
        return null;
    }

    private static bool IsValidCurrency(string value)
        => value.Trim().Length == 3 && value.Trim().All(char.IsAsciiLetter);

    private static bool IsValidTimeZone(string value)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

public sealed record ProfilePageState(
    string Email,
    string DisplayName,
    string BusinessName,
    string AboutText,
    string GreetingText,
    string Timezone,
    string Currency,
    int SlotStep,
    int Buffer,
    bool BookingEnabled);
