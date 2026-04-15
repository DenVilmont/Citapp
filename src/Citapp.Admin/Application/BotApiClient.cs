using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;

namespace Citapp.Admin.Application;

public class BotApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AdminSession _session;

    public BotApiClient(IHttpClientFactory httpClientFactory, AdminSession session)
    {
        _httpClientFactory = httpClientFactory;
        _session = session;
    }

    public async Task<List<TimeSlotVm>> GetSlotsAsync(Guid serviceId, DateOnly date)
    {
        var client = CreateAuthedClient();
        return await client.GetFromJsonAsync<List<TimeSlotVm>>($"/api/slots?serviceId={serviceId}&date={date:yyyy-MM-dd}") ?? new();
    }

    public async Task<BookingDto?> CreateBookingAsync(CreateBookingDto dto)
    {
        var client = CreateAuthedClient();
        var response = await client.PostAsJsonAsync("/api/bookings", dto);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Booking API error ({(int)response.StatusCode}): {text}");
        }

        return await response.Content.ReadFromJsonAsync<BookingDto>();
    }

    public async Task UpdateBookingStatusAsync(Guid bookingId, BookingStatus status)
    {
        var client = CreateAuthedClient();
        var normalizedStatus = status == BookingStatus.BlockedByMaster ? "blocked_by_master" : status.ToString().ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/bookings/{bookingId}/status?status={normalizedStatus}")
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private HttpClient CreateAuthedClient()
    {
        var client = _httpClientFactory.CreateClient("BotApi");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
        return client;
    }
}

public record TimeSlotVm(DateTimeOffset StartAt, DateTimeOffset EndAt, string Label);
