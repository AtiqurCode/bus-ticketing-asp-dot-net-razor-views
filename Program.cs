using System.Globalization;
using BusTicketing.Components;
using BusTicketing.Data;
using BusTicketing.Data.Seed;
using BusTicketing.Domain;
using BusTicketing.Services;
using BusTicketing.Services.Admin;
using BusTicketing.Services.Auth;
using BusTicketing.Services.Bookings;
using BusTicketing.Services.Localization;
using BusTicketing.Services.Scheduling;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// --- Database ----------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

builder.Services.AddDbContextFactory<AppDbContext>(options => options
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
    .UseSnakeCaseNamingConvention());

// Identity's stores resolve AppDbContext directly, so hand them a scoped
// instance drawn from the same pooled factory.
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// --- Identity (staff & admin only) --------------------------------
builder.Services
    .AddIdentityCore<StaffUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 8;
    })
    .AddRoles<StaffRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
{
    options.LoginPath = "/staff/login";
    options.LogoutPath = "/staff/logout";
    options.AccessDeniedPath = "/staff/denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(10);
    options.SlidingExpiration = true;
    options.Cookie.Name = "busticketing.staff";
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.BackOffice, p => p.RequireRole(Roles.SuperAdmin, Roles.CounterStaff))
    .AddPolicy(AuthPolicies.SuperAdmin, p => p.RequireRole(Roles.SuperAdmin));

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingStateProvider>();

// --- Localization ------------------------------------------------
// SharedResource lives in the BusTicketing.Resources namespace and its .resx
// sits at Resources/, so the resource base name already matches — no ResourcesPath.
builder.Services.AddLocalization();

var supportedCultures = builder.Configuration
    .GetSection("Localization:SupportedCultures").Get<string[]>() ?? ["en"];
var defaultCulture = builder.Configuration["Localization:DefaultCulture"] ?? "en";

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = supportedCultures.Select(c => new CultureInfo(c)).ToArray();
    options.DefaultRequestCulture = new RequestCulture(defaultCulture);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.ApplyCurrentCultureToResponseHeaders = true;
});

// --- Application services --------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<IAppClock, AppClock>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<BusService>();
builder.Services.AddScoped<RouteService>();
builder.Services.AddScoped<StaffService>();
builder.Services.AddScoped<ScheduleTemplateService>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<TripGenerationService>();
builder.Services.AddHostedService<TripGenerationBackgroundService>();

builder.Services.AddSingleton<SeatMapBroadcaster>();
builder.Services.AddScoped<TripSearchService>();
builder.Services.AddScoped<SeatHoldService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<BookingAdminService>();
builder.Services.AddScoped<PaymentReviewService>();
builder.Services.AddHostedService<BookingMaintenanceBackgroundService>();

// --- Blazor --------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await DatabaseInitializer.RunAsync(app.Services, app.Environment);

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapCultureEndpoints();
app.MapHealthEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
