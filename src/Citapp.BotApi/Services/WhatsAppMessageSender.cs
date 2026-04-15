using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Citapp.BotApi.Services;

public class WhatsAppMessageSender
{
    private static readonly TimeSpan CustomerServiceWindow = TimeSpan.FromHours(24);
    private static readonly AsyncLocal<OutboundSendContext?> SendContext = new();
    private readonly HttpClient _http;
    private readonly ILogger<WhatsAppMessageSender> _logger;
    public WhatsAppMessageSender(HttpClient http, ILogger<WhatsAppMessageSender> logger)
    {
        _http = http;
        _logger = logger;
    }

    public IDisposable BeginOutboundScope(Guid tenantId, Guid customerId, string waUserId, DateTimeOffset? customerLastSeenAt)
    {
        var previousContext = SendContext.Value;
        SendContext.Value = new OutboundSendContext(tenantId, customerId, waUserId, customerLastSeenAt);
        return new Scope(() => SendContext.Value = previousContext);
    }

    public Task<OutboundSendResult> SendTextAsync(string to, string phoneNumberId, string text)
        => SendAsync(phoneNumberId, new WhatsAppTextRequest(to, new WhatsAppTextBody(text)));

    public Task<OutboundSendResult> SendButtonsAsync(string to, string phoneNumberId, string body, List<(string Id, string Title)> buttons)
    {
        if (buttons.Count > 3) throw new ArgumentException("WhatsApp buttons limit is 3.");
        var request = new WhatsAppInteractiveRequest(
            to,
            new WhatsAppInteractiveBody(
                "button",
                new WhatsAppAction(
                    Buttons: buttons.Select(x => new WhatsAppReplyButton("reply", new WhatsAppReplyButtonData(x.Id, x.Title))).ToList()),
                new WhatsAppTextHeader(body)));

        return SendAsync(phoneNumberId, request);
    }

    public Task<OutboundSendResult> SendListAsync(string to, string phoneNumberId, string body, string buttonLabel, List<(string Id, string Title, string Description)> rows)
    {
        if (rows.Count > 10) throw new ArgumentException("WhatsApp list rows limit is 10.");
        var request = new WhatsAppInteractiveRequest(
            to,
            new WhatsAppInteractiveBody(
                "list",
                new WhatsAppAction(
                    Button: buttonLabel,
                    Sections:
                    [
                        new WhatsAppListSection(
                            "Выбор",
                            rows.Select(x => new WhatsAppListRow(x.Id, x.Title, x.Description)).ToList())
                    ]),
                new WhatsAppTextHeader(body)));

        return SendAsync(phoneNumberId, request);
    }

    public Task<OutboundSendResult> SendImageAsync(string to, string phoneNumberId, string imageUrl, string caption)
        => SendAsync(phoneNumberId, new WhatsAppImageRequest(to, new WhatsAppImageBody(imageUrl, caption)));

    private async Task<OutboundSendResult> SendAsync(string phoneNumberId, object request)
    {
        var context = SendContext.Value;
        if (!IsInsideCustomerServiceWindow(context?.CustomerLastSeenAt))
        {
            _logger.LogWarning(
                "Skipping WhatsApp outbound message outside customer service window. tenant_id={tenantId}, customer_id={customerId}, wa_user_id={waUserId}, last_seen_at={lastSeenAt}, phone_number_id={phoneNumberId}, request_type={requestType}",
                context?.TenantId,
                context?.CustomerId,
                context?.WaUserId,
                context?.CustomerLastSeenAt,
                phoneNumberId,
                request.GetType().Name);
            return new OutboundSendResult(OutboundSendOutcome.SkippedOutsideCustomerServiceWindow);
        }

        var token = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN") ?? string.Empty;
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        try
        {
            var response = await _http.PostAsJsonAsync($"https://graph.facebook.com/v19.0/{phoneNumberId}/messages", request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var isRestrictionError = responseBody.Contains("24", StringComparison.OrdinalIgnoreCase)
                    || responseBody.Contains("window", StringComparison.OrdinalIgnoreCase)
                    || responseBody.Contains("policy", StringComparison.OrdinalIgnoreCase);
                if (isRestrictionError)
                {
                    _logger.LogWarning(
                        "WhatsApp outbound send rejected by Meta restriction. tenant_id={tenantId}, customer_id={customerId}, wa_user_id={waUserId}, phone_number_id={phoneNumberId}, status={statusCode}, body={responseBody}",
                        context?.TenantId,
                        context?.CustomerId,
                        context?.WaUserId,
                        phoneNumberId,
                        (int)response.StatusCode,
                        responseBody);
                }
                else
                {
                    _logger.LogError(
                        "WhatsApp outbound send failed. tenant_id={tenantId}, customer_id={customerId}, wa_user_id={waUserId}, phone_number_id={phoneNumberId}, status={statusCode}, body={responseBody}",
                        context?.TenantId,
                        context?.CustomerId,
                        context?.WaUserId,
                        phoneNumberId,
                        (int)response.StatusCode,
                        responseBody);
                }

                return new OutboundSendResult(OutboundSendOutcome.Failed);
            }

            var metaResponse = JsonSerializer.Deserialize<WhatsAppSendResponse>(responseBody);
            if (metaResponse?.Messages is null || metaResponse.Messages.Count == 0 || string.IsNullOrWhiteSpace(metaResponse.Messages[0].Id))
            {
                _logger.LogError(
                    "WhatsApp send returned unexpected payload. tenant_id={tenantId}, customer_id={customerId}, wa_user_id={waUserId}, phone_number_id={phoneNumberId}, body={responseBody}",
                    context?.TenantId,
                    context?.CustomerId,
                    context?.WaUserId,
                    phoneNumberId,
                    responseBody);

                return new OutboundSendResult(OutboundSendOutcome.Failed);
            }

            _logger.LogInformation(
                "WhatsApp outbound send succeeded. tenant_id={tenantId}, customer_id={customerId}, wa_user_id={waUserId}, phone_number_id={phoneNumberId}, message_id={messageId}, request_type={requestType}",
                context?.TenantId,
                context?.CustomerId,
                context?.WaUserId,
                phoneNumberId,
                metaResponse.Messages[0].Id,
                request.GetType().Name);

            return new OutboundSendResult(OutboundSendOutcome.Sent);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "WhatsApp outbound send threw exception. tenant_id={tenantId}, customer_id={customerId}, wa_user_id={waUserId}, phone_number_id={phoneNumberId}",
                context?.TenantId,
                context?.CustomerId,
                context?.WaUserId,
                phoneNumberId);

            return new OutboundSendResult(OutboundSendOutcome.Failed);
        }
    }

    private static bool IsInsideCustomerServiceWindow(DateTimeOffset? customerLastSeenAt)
        => customerLastSeenAt.HasValue && DateTimeOffset.UtcNow - customerLastSeenAt.Value <= CustomerServiceWindow;

    private sealed record OutboundSendContext(Guid TenantId, Guid CustomerId, string WaUserId, DateTimeOffset? CustomerLastSeenAt);

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private readonly Action _onDispose = onDispose;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose();
        }
    }
}

public enum OutboundSendOutcome
{
    Sent = 1,
    SkippedOutsideCustomerServiceWindow = 2,
    Failed = 3
}

public readonly record struct OutboundSendResult(OutboundSendOutcome Outcome)
{
    public bool IsSent => Outcome == OutboundSendOutcome.Sent;
}

public record WhatsAppTextRequest(
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("text")] WhatsAppTextBody Text)
{
    [JsonPropertyName("messaging_product")] public string MessagingProduct => "whatsapp";
    [JsonPropertyName("type")] public string Type => "text";
}

public record WhatsAppImageRequest(
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("image")] WhatsAppImageBody Image)
{
    [JsonPropertyName("messaging_product")] public string MessagingProduct => "whatsapp";
    [JsonPropertyName("type")] public string Type => "image";
}

public record WhatsAppInteractiveRequest(
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("interactive")] WhatsAppInteractiveBody Interactive)
{
    [JsonPropertyName("messaging_product")] public string MessagingProduct => "whatsapp";
    [JsonPropertyName("type")] public string Type => "interactive";
}

public record WhatsAppTextBody([property: JsonPropertyName("body")] string Body);
public record WhatsAppImageBody([property: JsonPropertyName("link")] string Link, [property: JsonPropertyName("caption")] string Caption);
public record WhatsAppInteractiveBody(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("action")] WhatsAppAction Action,
    [property: JsonPropertyName("body")] WhatsAppTextHeader Body);

public record WhatsAppTextHeader([property: JsonPropertyName("text")] string Text);

public record WhatsAppAction(
    [property: JsonPropertyName("button")] string? Button = null,
    [property: JsonPropertyName("buttons")] List<WhatsAppReplyButton>? Buttons = null,
    [property: JsonPropertyName("sections")] List<WhatsAppListSection>? Sections = null);

public record WhatsAppReplyButton(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("reply")] WhatsAppReplyButtonData Reply);

public record WhatsAppReplyButtonData(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title);

public record WhatsAppListSection(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("rows")] List<WhatsAppListRow> Rows);

public record WhatsAppListRow(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description);

public record WhatsAppSendResponse([property: JsonPropertyName("messages")] List<WhatsAppSendMessage>? Messages);
public record WhatsAppSendMessage([property: JsonPropertyName("id")] string Id);
