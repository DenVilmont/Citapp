using System.Security.Claims;
using System.Text;
using Citapp.BotApi.Services;
using Citapp.BotApi.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
builder.Host.UseSerilog();

var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? string.Empty;
var serviceRole = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY") ?? string.Empty;
var corsOrigin = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGIN") ?? "*";

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.MetadataAddress = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = ClaimTypes.NameIdentifier
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient<WhatsAppMessageSender>();
builder.Services.AddSingleton(new Supabase.Client(supabaseUrl, serviceRole));
builder.Services.AddScoped<TenantRepository>();
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddScoped<BookingRepository>();
builder.Services.AddScoped<ScheduleRepository>();
builder.Services.AddScoped<ConversationStateRepository>();
builder.Services.AddScoped<WebhookEventRepository>();
builder.Services.AddScoped<SlotCalculationService>();
builder.Services.AddScoped<BookingTransactionService>();
builder.Services.AddScoped<BotFsmHandler>();
builder.Services.AddScoped<WebhookProcessor>();

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/webhook/whatsapp", (HttpRequest request) =>
{
    var mode = request.Query["hub.mode"].ToString();
    var verifyToken = request.Query["hub.verify_token"].ToString();
    var challenge = request.Query["hub.challenge"].ToString();
    var expected = Environment.GetEnvironmentVariable("WHATSAPP_VERIFY_TOKEN") ?? "test";
    return mode == "subscribe" && verifyToken == expected ? Results.Text(challenge) : Results.Unauthorized();
});

app.MapPost("/webhook/whatsapp", async (HttpRequest request, WebhookProcessor processor) =>
    await processor.ProcessAsync(request));

app.MapGet("/api/slots", async (Guid serviceId, DateOnly date, Guid tenantId, SlotCalculationService svc)
    => Results.Ok(await svc.GetAvailableSlotsAsync(tenantId, serviceId, date))).RequireAuthorization();

app.MapGet("/api/slots/available-dates", async (Guid serviceId, Guid tenantId, SlotCalculationService svc)
    => Results.Ok(await svc.GetAvailableDatesAsync(tenantId, serviceId, 30))).RequireAuthorization();

app.MapPost("/api/bookings", async (Citapp.Shared.DTOs.CreateBookingDto dto, BookingTransactionService svc) =>
{
    try { return Results.Created($"/api/bookings/{Guid.NewGuid()}", await svc.CreateAsync(dto)); }
    catch (BookingConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
}).RequireAuthorization();

app.MapPut("/api/bookings/{id:guid}/status", async (Guid id, string status, Guid tenantId, BookingRepository repo) =>
{
    await repo.UpdateStatusAsync(id, tenantId, status);
    return Results.NoContent();
}).RequireAuthorization();

app.Run();
