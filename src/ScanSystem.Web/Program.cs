using Microsoft.AspNetCore.Http.Features;
using ScanSystem.Shared.Data;
using ScanSystem.Web.Components;
using ScanSystem.Web.Hubs;
using ScanSystem.Web.Services;

var builder = WebApplication.CreateBuilder(args);

const long MaxRequestBodyBytes = 100L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxRequestBodyBytes);

// ───────────────────────── Data Access (Dapper + DataTable/DataRow) ─────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection String با کلید 'Default' در appsettings.json یافت نشد.");

builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<ScanDataAccess>();

// ───────────────────────── SignalR + Application Services ─────────────────────────
builder.Services.AddSingleton<AgentConnectionRegistry>();
builder.Services.AddScoped<IScanService, ScanService>();

builder.Services.AddSignalR(opt =>
{
    opt.MaximumReceiveMessageSize = 50 * 1024 * 1024;
    opt.StreamBufferCapacity = 20;
    opt.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
    opt.KeepAliveInterval = TimeSpan.FromSeconds(15);
    opt.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

// ───────────────────────── Blazor Server ─────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<ScanHub>("/scanhub");

app.Run();
