using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NoteKeeper.Data;
using NoteKeeper.Services;

var environmentName =
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = string.Equals(
        environmentName,
        Environments.Development,
        StringComparison.OrdinalIgnoreCase)
        ? Directory.GetCurrentDirectory()
        : AppContext.BaseDirectory
});

// Build output can be launched directly outside the Development environment.
// Keep the static web assets manifest enabled in addition to copying wwwroot
// into the build output so direct executable launches work reliably.
builder.WebHost.UseStaticWebAssets();

var hasExplicitUrlOverride =
    args.Any(argument =>
        argument.Equals("--urls", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase)) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_URLS"));

if (!hasExplicitUrlOverride)
{
    builder.WebHost.UseUrls(
        "https://localhost:7147",
        "http://localhost:5147");
}

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("en"),
        new CultureInfo("it"),
        new CultureInfo("uk")
    };

    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders = [new CookieRequestCultureProvider()];
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("The DefaultConnection connection string is missing.");

var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
if (!string.IsNullOrWhiteSpace(sqliteConnection.DataSource) &&
    !Path.IsPathRooted(sqliteConnection.DataSource))
{
    sqliteConnection.DataSource = Path.GetFullPath(
        sqliteConnection.DataSource,
        builder.Environment.ContentRootPath);
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConnection.ConnectionString));

builder.Services.AddSingleton<PriorityTagService>();
builder.Services.AddHostedService<WindowsTrayIconService>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Notes}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.Run();
