using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;
using Microsoft.AspNetCore.Components.Forms;

namespace Citapp.Admin.Application;

public sealed class ServicePageService
{
    private readonly IServiceRepository _serviceRepository;

    public ServicePageService(IServiceRepository serviceRepository)
    {
        _serviceRepository = serviceRepository;
    }

    public async Task<ServicePageData> LoadAsync(Guid tenantId)
    {
        var services = await _serviceRepository.GetAllAsync(tenantId);
        var mediaByServiceId = new Dictionary<Guid, List<ServiceMediaDto>>();
        foreach (var service in services)
        {
            mediaByServiceId[service.Id] = await _serviceRepository.GetMediaByServiceAsync(tenantId, service.Id);
        }

        return new ServicePageData(services, mediaByServiceId);
    }

    public async Task<string?> SaveAsync(Guid tenantId, ServiceEditState state, int currentCount, int? currentSortOrder)
    {
        var validationError = Validate(state);
        if (validationError is not null)
        {
            return validationError;
        }

        var sortOrder = state.EditingId is null ? currentCount : currentSortOrder ?? currentCount;
        var dto = new ServiceDto(
            state.EditingId ?? Guid.Empty,
            tenantId,
            state.Name.Trim(),
            string.IsNullOrWhiteSpace(state.Description) ? null : state.Description.Trim(),
            state.Duration,
            state.Price,
            state.Currency.Trim().ToUpperInvariant(),
            state.IsActive,
            sortOrder,
            DateTimeOffset.UtcNow);

        if (state.EditingId is null)
        {
            await _serviceRepository.CreateAsync(dto, tenantId);
        }
        else
        {
            await _serviceRepository.UpdateAsync(state.EditingId.Value, dto, tenantId);
        }

        return null;
    }

    public async Task ToggleActiveAsync(Guid tenantId, Guid serviceId, bool targetIsActive)
        => await _serviceRepository.SetActiveAsync(serviceId, tenantId, targetIsActive);

    public async Task UpdateSortOrdersAsync(Guid tenantId, IReadOnlyList<Guid> orderedServiceIds)
        => await _serviceRepository.UpdateSortOrdersAsync(tenantId, orderedServiceIds);

    public async Task<string?> UploadMediaAsync(Guid tenantId, Guid serviceId, IBrowserFile? file, string accessToken)
    {
        if (file is null)
        {
            return null;
        }

        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "Only image files are supported.";
        }

        await using var stream = file.OpenReadStream(5 * 1024 * 1024);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        await _serviceRepository.UploadMediaAsync(tenantId, serviceId, file.Name, file.ContentType, ms.ToArray(), accessToken);
        return null;
    }

    public async Task<List<ServiceMediaDto>> GetMediaAsync(Guid tenantId, Guid serviceId)
        => await _serviceRepository.GetMediaByServiceAsync(tenantId, serviceId);

    public async Task SetPrimaryAsync(Guid tenantId, Guid serviceId, Guid mediaId)
        => await _serviceRepository.SetPrimaryMediaAsync(tenantId, serviceId, mediaId);

    public async Task DeleteMediaAsync(Guid tenantId, Guid serviceId, Guid mediaId, string accessToken)
        => await _serviceRepository.DeleteMediaAsync(tenantId, serviceId, mediaId, accessToken);

    private static string? Validate(ServiceEditState state)
    {
        if (string.IsNullOrWhiteSpace(state.Name)) return "Name is required.";
        if (state.Duration <= 0 || state.Duration > 480) return "Duration must be between 1 and 480.";
        if (state.Price < 0) return "Price cannot be negative.";
        if (!IsValidCurrency(state.Currency)) return "Use a valid 3-letter currency code.";
        return null;
    }

    private static bool IsValidCurrency(string value)
        => value.Trim().Length == 3 && value.Trim().All(char.IsAsciiLetter);
}

public sealed record ServicePageData(
    List<ServiceDto> Services,
    Dictionary<Guid, List<ServiceMediaDto>> MediaByServiceId);

public sealed record ServiceEditState(
    Guid? EditingId,
    string Name,
    string Description,
    int Duration,
    decimal Price,
    string Currency,
    bool IsActive);
