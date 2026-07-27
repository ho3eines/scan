using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Repositories;
using ScanSystem.Web.Components;
using ScanSystem.Web.Hubs;
using ScanSystem.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ───────────────────────── محدودیت حجم آپلود (جایگزینی تصویر / اسکن‌های بزرگ) ─────────────────────────
const long MaxRequestBodyBytes = 100L * 1024 * 1024; // 100MB
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxRequestBodyBytes);

// ───────────────────────── Data Access (Dapper — بدون EF Core) ─────────────────────────
// Connection String از appsettings.json خوانده می‌شود و به Factory تزریق می‌گردد.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection String با کلید 'Default' در appsettings.json یافت نشد.");

builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqlConnectionFactory(connectionString));

// ثبت Repositoryها (هرکدام Scoped چون هر request باید Connection مستقل داشته باشد).
builder.Services.AddScoped<IAgentRepository, AgentRepository>();
builder.Services.AddScoped<IScanRequestRepository, ScanRequestRepository>();
builder.Services.AddScoped<IImageRepository, ImageRepository>();
builder.Services.AddScoped<IImageGroupRepository, ImageGroupRepository>();

//(Application Service — منطق هماهنگ‌کننده بین Hub، Repositoryها و نگاشت SignalR)
builder.Services.AddScoped<IScanService, ScanService>();
builder.Services.AddSingleton<AgentConnectionRegistry>(); // نگاشت MachineName <-> ConnectionId در حافظه

// ───────────────────────── SignalR + API Controllers ─────────────────────────
builder.Services.AddSignalR(opt =>
{
    // 50MB برای اسکن‌های چند صفحه‌ای با تصاویر بزرگ
    opt.MaximumReceiveMessageSize = 50 * 1024 * 1024;
    // چرخه FlushResults به‌سرعت برای streaming صفحات
    opt.StreamBufferCapacity = 20;
});
builder.Services.AddControllers();

// ───────────────────────── Blazor Server ─────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// HttpClient برای فراخوانی API داخلی از Razor (گالری/اسکن)
builder.Services.AddHttpClient("api", c =>
{
    // در Blazor Server، آدرس نسبی به AbsoluteUri تبدیل می‌شود؛ یک HttpClient معمولی هم کافی است.
});
builder.Services.AddScoped<HttpClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("api");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapControllers();                                  // /api/...
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<ScanHub>("/scanhub");                       // ارتباط Blazor UI و WPF Agent

app.Run();
