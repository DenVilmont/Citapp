using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;

namespace Citapp.Admin.Application;

public sealed class CustomerPageService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly ITenantRepository _tenantRepository;

    public CustomerPageService(
        ICustomerRepository customerRepository,
        IBookingRepository bookingRepository,
        IServiceRepository serviceRepository,
        ITenantRepository tenantRepository)
    {
        _customerRepository = customerRepository;
        _bookingRepository = bookingRepository;
        _serviceRepository = serviceRepository;
        _tenantRepository = tenantRepository;
    }

    public async Task<List<CustomerDto>> SearchAsync(Guid tenantId, string query)
        => await _customerRepository.SearchAsync(tenantId, query);

    public async Task SaveNoteAsync(Guid tenantId, Guid customerId, string note)
        => await _customerRepository.UpdateNoteAsync(customerId, tenantId, note);

    public async Task<TimeZoneInfo> LoadTenantTimeZoneAsync(Guid tenantId)
    {
        var tenant = await _tenantRepository.GetAsync(tenantId);
        return BookingPageService.ResolveTimeZone(tenant?.Timezone);
    }

    public async Task<CustomerCardData?> LoadCustomerCardAsync(Guid tenantId, Guid customerId)
    {
        var customer = await _customerRepository.GetByIdAsync(tenantId, customerId);
        if (customer is null)
        {
            return null;
        }

        var history = await _bookingRepository.GetByCustomerAsync(tenantId, customerId);
        var services = await _serviceRepository.GetByIdsAsync(tenantId, history.Select(x => x.ServiceId));
        var labels = services.ToDictionary(x => x.Id, x => x.Name);
        return new CustomerCardData(customer, history, labels);
    }
}

public sealed record CustomerCardData(
    CustomerDto Customer,
    List<BookingDto> History,
    Dictionary<Guid, string> ServiceLabels);
