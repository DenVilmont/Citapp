using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;

namespace Citapp.Admin.Application;

public sealed class BookingPageService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly BotApiClient _botApiClient;

    public BookingPageService(
        IBookingRepository bookingRepository,
        ICustomerRepository customerRepository,
        IServiceRepository serviceRepository,
        ITenantRepository tenantRepository,
        BotApiClient botApiClient)
    {
        _bookingRepository = bookingRepository;
        _customerRepository = customerRepository;
        _serviceRepository = serviceRepository;
        _tenantRepository = tenantRepository;
        _botApiClient = botApiClient;
    }

    public async Task<BookingPageInitData> LoadInitAsync(Guid tenantId)
    {
        var tenant = await _tenantRepository.GetAsync(tenantId);
        var tenantTimeZone = ResolveTimeZone(tenant?.Timezone);
        var allServices = await _serviceRepository.GetAllAsync(tenantId);
        var activeServices = allServices.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList();
        return new BookingPageInitData(tenantTimeZone, activeServices);
    }

    public async Task<BookingListData> LoadBookingsAsync(Guid tenantId, string fromText, string toText, string statusText)
    {
        BookingStatus? status = null;
        if (Enum.TryParse<BookingStatus>(statusText, true, out var parsed))
        {
            status = parsed;
        }

        var bookings = await _bookingRepository.GetForRangeAsync(tenantId, DateOnly.Parse(fromText), DateOnly.Parse(toText), status);

        var customerLabels = new Dictionary<Guid, string>();
        var serviceLabels = new Dictionary<Guid, string>();

        var customerIds = bookings.Select(x => x.CustomerId).Distinct().ToList();
        var serviceIds = bookings.Select(x => x.ServiceId).Distinct().ToList();
        var customers = await _customerRepository.GetByIdsAsync(tenantId, customerIds);
        var services = await _serviceRepository.GetByIdsAsync(tenantId, serviceIds);

        foreach (var c in customers)
        {
            customerLabels[c.Id] = string.IsNullOrWhiteSpace(c.Phone) ? $"{c.DisplayName} ({c.WaUserId})" : $"{c.DisplayName} ({c.Phone})";
        }

        foreach (var s in services)
        {
            serviceLabels[s.Id] = s.Name;
        }

        return new BookingListData(bookings, customerLabels, serviceLabels);
    }

    public async Task<List<CustomerDto>> SearchCustomersAsync(Guid tenantId, string query)
        => await _customerRepository.SearchAsync(tenantId, query);

    public async Task<(CustomerDto? Customer, string? Error)> CreateManualCustomerAsync(Guid tenantId, string displayName, string? phone)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (null, "Display name is required for a new customer.");
        }

        var created = await _customerRepository.CreateManualAsync(tenantId, displayName.Trim(), phone);
        return (created, null);
    }

    public async Task<(List<TimeSlotVm> Slots, string? Error)> LoadSlotsAsync(Guid serviceId, string slotDateText)
    {
        if (serviceId == Guid.Empty)
        {
            return (new List<TimeSlotVm>(), "Select a service first.");
        }

        return (await _botApiClient.GetSlotsAsync(serviceId, DateOnly.Parse(slotDateText)), null);
    }

    public async Task<string?> CreateBookingAsync(BookingCreateInput input, IReadOnlyList<ServiceDto> activeServices, IReadOnlyList<TimeSlotVm> slots)
    {
        var service = activeServices.FirstOrDefault(x => x.Id == input.ServiceId);
        var slot = slots.FirstOrDefault(x => x.StartAt == input.SlotStartAt);
        if (input.CustomerId == Guid.Empty || input.ServiceId == Guid.Empty || input.SlotStartAt == default)
        {
            return "Select customer, service, and slot.";
        }

        if (service is null || slot is null)
        {
            return "Could not resolve service or slot.";
        }

        var dto = new CreateBookingDto(
            input.CustomerId,
            input.ServiceId,
            BookingSource.Admin,
            DateOnly.FromDateTime(slot.StartAt.Date),
            slot.StartAt,
            slot.EndAt,
            service.DurationMinutes,
            service.PriceAmount,
            service.Currency);

        await _botApiClient.CreateBookingAsync(dto);
        return null;
    }

    public async Task UpdateStatusAsync(Guid bookingId, BookingStatus status)
        => await _botApiClient.UpdateBookingStatusAsync(bookingId, status);

    public static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

public sealed record BookingPageInitData(TimeZoneInfo TenantTimeZone, List<ServiceDto> ActiveServices);
public sealed record BookingListData(
    List<BookingDto> Bookings,
    Dictionary<Guid, string> CustomerLabels,
    Dictionary<Guid, string> ServiceLabels);
public sealed record BookingCreateInput(Guid CustomerId, Guid ServiceId, DateTimeOffset SlotStartAt);
