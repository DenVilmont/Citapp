using Citapp.Admin.Application;
using Citapp.Admin.Domain.Ports;
using Citapp.Admin.Infrastructure.Repositories;
using Supabase;
using System.Text;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

using (var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
{
    var runtimeConfigJson = await http.GetStringAsync("appsettings.runtime.json");
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(runtimeConfigJson));
    builder.Configuration.AddJsonStream(stream);
}

var config = builder.Configuration;
var supabaseUrl = config["Supabase:Url"] ?? string.Empty;
var supabaseAnonKey = config["Supabase:AnonKey"] ?? string.Empty;
var botApiBaseUrl = config["BotApi:BaseUrl"] ?? string.Empty;

if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseAnonKey) || string.IsNullOrWhiteSpace(botApiBaseUrl))
{
    throw new InvalidOperationException("Missing runtime config. Set Supabase:Url, Supabase:AnonKey and BotApi:BaseUrl in appsettings.runtime.json.");
}

builder.Services.AddScoped(_ => new Client(supabaseUrl, supabaseAnonKey, new SupabaseOptions
{
    AutoConnectRealtime = false,
    AutoRefreshToken = true
}));

builder.Services.AddHttpClient("BotApi", c => c.BaseAddress = new Uri(botApiBaseUrl));

builder.Services.AddScoped<AdminSession>();
builder.Services.AddScoped<BotApiClient>();

builder.Services.AddScoped<IServiceRepository, ServiceRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IScheduleRepository, ScheduleRepository>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<IProfileRepository, ProfileRepository>();

await builder.Build().RunAsync();
