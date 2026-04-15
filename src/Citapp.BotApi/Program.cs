using System.Security.Claims;
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
var corsOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins(
                    "http://localhost:5000",
                    "https://localhost:5001",
                    "http://localhost:5173",
                    "https://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
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
builder.Services.AddScoped<ServiceRepository>();
builder.Services.AddScoped<ConversationStateRepository>();
builder.Services.AddScoped<WebhookEventRepository>();
builder.Services.AddScoped<AuthenticatedTenantResolver>();
builder.Services.AddScoped<SlotCalculationService>();
builder.Services.AddScoped<BookingTransactionService>();
builder.Services.AddScoped<BotFsmHandler>();
builder.Services.AddScoped<WebhookProcessor>();
builder.Services.AddHostedService<ConversationStateCleanupService>();

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

app.MapGet("/api/slots", async (ClaimsPrincipal user, Guid serviceId, DateOnly date, AuthenticatedTenantResolver tenantResolver, SlotCalculationService svc) =>
{
    var tenantId = await tenantResolver.ResolveTenantIdAsync(user);
    if (tenantId is null) return Results.Forbid();
    return Results.Ok(await svc.GetAvailableSlotsAsync(tenantId.Value, serviceId, date));
}).RequireAuthorization();

app.MapGet("/api/slots/available-dates", async (ClaimsPrincipal user, Guid serviceId, AuthenticatedTenantResolver tenantResolver, SlotCalculationService svc) =>
{
    var tenantId = await tenantResolver.ResolveTenantIdAsync(user);
    if (tenantId is null) return Results.Forbid();
    return Results.Ok(await svc.GetAvailableDatesAsync(tenantId.Value, serviceId, 30));
}).RequireAuthorization();

app.MapPost("/api/bookings", async (ClaimsPrincipal user, Citapp.Shared.DTOs.CreateBookingDto dto, AuthenticatedTenantResolver tenantResolver, BookingTransactionService svc) =>
{
    var tenantId = await tenantResolver.ResolveTenantIdAsync(user);
    if (tenantId is null) return Results.Forbid();

    var rawUserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    Guid? createdByUserId = Guid.TryParse(rawUserId, out var userId) ? userId : null;

    try
    {
        var created = await svc.CreateAsync(tenantId.Value, dto, createdByUserId);
        return Results.Created($"/api/bookings/{created.Id}", created);
    }
    catch (BookingConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
}).RequireAuthorization();

app.MapPut("/api/bookings/{id:guid}/status", async (ClaimsPrincipal user, Guid id, string status, AuthenticatedTenantResolver tenantResolver, BookingTransactionService svc) =>
{
    var tenantId = await tenantResolver.ResolveTenantIdAsync(user);
    if (tenantId is null) return Results.Forbid();

    try
    {
        await svc.UpdateStatusAsync(tenantId.Value, id, status);
        return Results.NoContent();
    }
    catch (BookingConflictException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
}).RequireAuthorization();

app.Run();
