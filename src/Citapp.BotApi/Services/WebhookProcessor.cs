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
            var msg = payload?.Entry?.FirstOrDefault()?.Changes?.FirstOrDefault()?.Value?.Messages?.FirstOrDefault();
            if (msg is null) return Results.Ok();

            var phoneNumberId = payload!.Entry[0].Changes[0].Value.Metadata.PhoneNumberId;
            var tenantId = await _tenants.GetByPhoneNumberIdAsync(phoneNumberId);
            var accepted = await _events.TrySaveAsync(msg.Id, tenantId, raw);
            if (!accepted) return Results.Ok();
            if (tenantId is null) { _logger.LogInformation("Unknown phone_number_id {phoneNumberId}", phoneNumberId); return Results.Ok(); }

            var contact = payload.Entry[0].Changes[0].Value.Contacts?.FirstOrDefault();
            var customer = await _customers.GetOrCreateAsync(tenantId.Value, msg.From, contact?.Profile?.Name ?? "Клиент");
            await _customers.UpdateLastSeenAsync(customer.Id, tenantId.Value);

            var interactiveType = msg.Interactive?.Type;
            var payloadId = msg.Interactive?.ButtonReply?.Id ?? msg.Interactive?.ListReply?.Id;
            await _fsm.HandleAsync(tenantId.Value, customer.Id, msg.From, phoneNumberId, msg.Type, interactiveType, payloadId, msg.Text?.Body);
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
}
