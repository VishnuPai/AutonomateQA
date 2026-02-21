using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using UiTestRunner.Configuration;
using UiTestRunner.Data;
using UiTestRunner.Services;
using UiTestRunner.AiProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Database
builder.Services.AddDbContext<UiTestRunner.Data.ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

// Hangfire
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseMemoryStorage());
builder.Services.AddHangfireServer();

// Rate Limiting (DoS Protection)
var rateLimitingSettings = builder.Configuration.GetSection(RateLimitingSettings.SectionName).Get<RateLimitingSettings>() ?? new RateLimitingSettings();
builder.Services.AddRateLimiter(options => {
    options.AddFixedWindowLimiter("TriggerTestPolicy", opt => {
        opt.Window = TimeSpan.FromMinutes(rateLimitingSettings.WindowMinutes);
        opt.PermitLimit = rateLimitingSettings.PermitLimit;
        opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = rateLimitingSettings.QueueLimit;
    });
});

// Register configuration objects
builder.Services.Configure<PlaywrightSettings>(builder.Configuration.GetSection(PlaywrightSettings.SectionName));
builder.Services.Configure<RateLimitingSettings>(builder.Configuration.GetSection(RateLimitingSettings.SectionName));
builder.Services.Configure<RunnerSettings>(builder.Configuration.GetSection(RunnerSettings.SectionName));

// Register Services
builder.Services.AddScoped<IUiTestService, UiTestService>();
builder.Services.AddScoped<IPlaywrightVisionService, PlaywrightVisionService>();
builder.Services.AddScoped<ITestRecorderService, TestRecorderService>();
builder.Services.AddScoped<ITestDataManager, TestDataManager>();

var aiProvider = builder.Configuration["AiProvider"];
if (string.Equals(aiProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IAiModelProvider, OpenAiProvider>();
}
else
{
    builder.Services.AddScoped<IAiModelProvider, GeminiProvider>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/TestRunner/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=TestRunner}/{action=Index}/{id?}")
    .WithStaticAssets();

app.UseHangfireDashboard();

// Run Database Migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UiTestRunner.Data.ApplicationDbContext>();
    // Replaces EnsureCreated() to allow incremental schema updates without dropping old data
    db.Database.Migrate();

    // Ensure GherkinScript column exists (fixes "no such column: t.GherkinScript" if migration wasn't applied)
    try
    {
        var hasColumn = db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) FROM pragma_table_info('TestResults') WHERE name='GherkinScript'").FirstOrDefault();
        if (hasColumn == 0)
            db.Database.ExecuteSqlRaw("ALTER TABLE TestResults ADD COLUMN GherkinScript TEXT NULL;");
    }
    catch (Exception)
    {
        // Ignore (e.g. non-SQLite or table not created yet)
    }
}


app.Run();
