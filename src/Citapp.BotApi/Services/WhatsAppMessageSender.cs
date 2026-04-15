using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Citapp.BotApi.Services;

public class WhatsAppMessageSender
{
    private readonly HttpClient _http;
    public WhatsAppMessageSender(HttpClient http) => _http = http;

    public Task SendTextAsync(string to, string phoneNumberId, string text)
        => SendAsync(phoneNumberId, new WhatsAppTextRequest(to, new WhatsAppTextBody(text)));

    public Task SendButtonsAsync(string to, string phoneNumberId, string body, List<(string Id, string Title)> buttons)
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

    public Task SendListAsync(string to, string phoneNumberId, string body, string buttonLabel, List<(string Id, string Title, string Description)> rows)
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

    public Task SendImageAsync(string to, string phoneNumberId, string imageUrl, string caption)
        => SendAsync(phoneNumberId, new WhatsAppImageRequest(to, new WhatsAppImageBody(imageUrl, caption)));

    private async Task SendAsync(string phoneNumberId, object request)
    {
        var token = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN") ?? string.Empty;
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await _http.PostAsJsonAsync($"https://graph.facebook.com/v19.0/{phoneNumberId}/messages", request);
    }
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
