using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Citapp.BotApi.Infrastructure.Repositories;
using Citapp.BotApi.Models;

namespace Citapp.BotApi.Services;

public class WebhookProcessor
{
    private readonly WebhookEventRepository _events;
    private readonly TenantRepository _tenants;
    private readonly CustomerRepository _customers;
    private readonly BotFsmHandler _fsm;
    private readonly ILogger<WebhookProcessor> _logger;

    public WebhookProcessor(WebhookEventRepository events, TenantRepository tenants, CustomerRepository customers, BotFsmHandler fsm, ILogger<WebhookProcessor> logger)
    {
        _events = events; _tenants = tenants; _customers = customers; _fsm = fsm; _logger = logger;
    }

    public async Task<IResult> ProcessAsync(HttpRequest request)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync();
            var signature = request.Headers["X-Hub-Signature-256"].ToString();
            if (!IsValidSignature(raw, signature)) return Results.Unauthorized();

            var payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(raw);
            if (payload?.Entry is null || payload.Entry.Count == 0) return Results.Ok();

            foreach (var entry in payload.Entry)
            {
                foreach (var change in entry.Changes ?? [])
                {
                    var phoneNumberId = change.Value?.Metadata?.PhoneNumberId;
                    if (string.IsNullOrWhiteSpace(phoneNumberId))
                    {
                        continue;
                    }

                    var tenantId = await _tenants.GetByPhoneNumberIdAsync(phoneNumberId);
                    if (tenantId is null)
                    {
                        _logger.LogInformation("Unknown phone_number_id {phoneNumberId}", phoneNumberId);
                    }

                    foreach (var status in change.Value?.Statuses ?? [])
                    {
                        await ProcessStatusAsync(status, tenantId, raw);
                    }

                    foreach (var msg in change.Value?.Messages ?? [])
                    {
                        await ProcessMessageAsync(msg, change.Value?.Contacts, tenantId, phoneNumberId, raw);
                    }
                }
            }

            return Results.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook processing failed");
            return Results.Ok();
        }
    }

    private static bool IsValidSignature(string raw, string signatureHeader)
    {
        var appSecret = Environment.GetEnvironmentVariable("WHATSAPP_APP_SECRET") ?? string.Empty;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(raw));
        var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        return expected == signatureHeader;
    }

    private async Task ProcessStatusAsync(MessageStatus status, Guid? tenantId, string rawPayload)
    {
        var eventId = BuildStatusEventId(status);
        var accepted = await _events.TrySaveAsync(eventId, tenantId, rawPayload);
        if (!accepted)
        {
            return;
        }

        await _events.MarkProcessedAsync(eventId, true);
    }

    private async Task ProcessMessageAsync(Message msg, List<Contact>? contacts, Guid? tenantId, string phoneNumberId, string rawPayload)
    {
        var eventId = BuildMessageEventId(msg);
        var accepted = await _events.TrySaveAsync(eventId, tenantId, rawPayload);
        if (!accepted)
        {
            return;
        }

        if (tenantId is null)
        {
            await _events.MarkProcessedAsync(eventId, true);
            return;
        }

        try
        {
            var contact = contacts?.FirstOrDefault(x => x.WaId == msg.From) ?? contacts?.FirstOrDefault();
            var displayName = contact?.Profile?.Name ?? "Клиент";
            var customer = await _customers.GetOrCreateAsync(tenantId.Value, msg.From, displayName);
            await _customers.UpdateProfileAsync(customer.Id, tenantId.Value, contact?.Profile?.Name, msg.From);
            await _customers.UpdateLastSeenAsync(customer.Id, tenantId.Value);

            var interactiveType = msg.Interactive?.Type;
            var payloadId = msg.Interactive?.ButtonReply?.Id ?? msg.Interactive?.ListReply?.Id;
            await _fsm.HandleAsync(tenantId.Value, customer.Id, msg.From, phoneNumberId, msg.Type, interactiveType, payloadId, msg.Text?.Body);
            await _events.MarkProcessedAsync(eventId, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message processing failed for message {messageId}", msg.Id);
            await _events.MarkProcessedAsync(eventId, false);
        }
    }

    private static string BuildMessageEventId(Message message) => $"message:{message.Id}";
    private static string BuildStatusEventId(MessageStatus status) => $"status:{status.Id}:{status.Status}:{status.Timestamp}";
}
