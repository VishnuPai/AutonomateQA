using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using UiTestRunner.Configuration;
using UiTestRunner.Data;
using UiTestRunner.Services;
using UiTestRunner.AiProviders;

// Set content root at creation time (required: changing it later via builder.Host is not supported).
// When running from bin\Debug or bin\Release (dotnet run / F5), use project directory so the same app.db is used
// (execution history and saved test scripts from before the winservice changes stay visible).
// When running as a published exe (e.g. Windows Service), use the exe directory.
var contentRoot = AppContext.BaseDirectory;
var baseDir = AppContext.BaseDirectory;
if ((baseDir.Contains("bin" + Path.DirectorySeparatorChar + "Debug", StringComparison.OrdinalIgnoreCase) ||
     baseDir.Contains("bin" + Path.DirectorySeparatorChar + "Release", StringComparison.OrdinalIgnoreCase)) &&
    !string.IsNullOrEmpty(Environment.CurrentDirectory) && Directory.Exists(Environment.CurrentDirectory))
    contentRoot = Environment.CurrentDirectory;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot
});

// Run as Windows Service when installed (enables SCM control; no change when run interactively)
builder.Host.UseWindowsService(options => options.ServiceName = "UiTestRunner");

// Use URL(s) from config so the same port works when run as a Windows Service (launchSettings.json is not used by the service)
var urls = builder.Configuration["Urls"]?.Trim();
if (!string.IsNullOrEmpty(urls))
    builder.WebHost.UseUrls(urls);

// Optional: load AI Hub config from local file (gitignored; for local testing only)
builder.Configuration.AddJsonFile("appsettings.AIHub.json", optional: true, reloadOnChange: false);
// Optional: load environment-specific URLs (SIT, ST, etc.) so they override appsettings.json "Environments" section
builder.Configuration.AddJsonFile("appsettings.SIT.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("appsettings.ST.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("appsettings.QA.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("appsettings.SPP.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("appsettings.PP.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: false);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Database: Sqlite (default) or SqlServer, driven by ConnectionStrings:DefaultConnection and Database:Provider
var dbProvider = builder.Configuration["Database:Provider"]?.Trim() ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")?.Trim();
if (string.IsNullOrEmpty(connectionString))
    connectionString = "Data Source=app.db";
// When using SQLite with a relative path, resolve to content root (project dir for dotnet run, exe dir for service)
if (!string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase) && connectionString.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
{
    var path = connectionString.Replace("Data Source=", "").Trim().TrimStart('"').TrimEnd('"').Trim();
    if (path.Length > 0 && !Path.IsPathRooted(path))
    {
        path = Path.Combine(contentRoot, path);
        connectionString = "Data Source=" + path;
    }
}
var isSqlServer = string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase);
builder.Services.AddDbContext<UiTestRunner.Data.ApplicationDbContext>(options =>
{
    if (isSqlServer)
        options.UseSqlServer(connectionString);
    else
        options.UseSqlite(connectionString);
});

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
builder.Services.AddScoped<ITestRunTokenTracker, TestRunTokenTracker>();
builder.Services.AddScoped<IFeatureFileService, FeatureFileService>();
builder.Services.AddTransient<UiTestRunner.Background.SequentialBatchJob>();

builder.Services.AddHttpClient();

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

// Run Database Migrations and log database location (helps when running as Windows Service to verify path and permissions)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UiTestRunner.Data.ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    if (!isSqlServer)
    {
        var ds = connectionString.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ? connectionString.Replace("Data Source=", "").Trim().Trim('"').Trim() : "";
        if (!string.IsNullOrEmpty(ds))
            logger.LogInformation("Database file: {Path} (Execution history and saved scenarios use this; ensure the process has read/write access.)", ds);
    }
    db.Database.Migrate();

    // SQLite-only fallback: ensure columns exist if an older DB predates migrations (pragma_table_info is SQLite-specific).
    if (!isSqlServer)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();
            long HasColumn(string columnName)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('TestResults') WHERE name='" + columnName.Replace("'", "''") + "'";
                var v = cmd.ExecuteScalar();
                return v is long l ? l : Convert.ToInt64(v ?? 0);
            }
            void AddColumnIfMissing(string name, string sql)
            {
                if (HasColumn(name) != 0) return;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            AddColumnIfMissing("GherkinScript", "ALTER TABLE TestResults ADD COLUMN GherkinScript TEXT NULL");
            AddColumnIfMissing("PromptTokens", "ALTER TABLE TestResults ADD COLUMN PromptTokens INTEGER NULL");
            AddColumnIfMissing("CompletionTokens", "ALTER TABLE TestResults ADD COLUMN CompletionTokens INTEGER NULL");
            AddColumnIfMissing("TotalTokens", "ALTER TABLE TestResults ADD COLUMN TotalTokens INTEGER NULL");
            AddColumnIfMissing("BatchRunId", "ALTER TABLE TestResults ADD COLUMN BatchRunId TEXT NULL");
            AddColumnIfMissing("FeaturePath", "ALTER TABLE TestResults ADD COLUMN FeaturePath TEXT NULL");
            AddColumnIfMissing("ScenarioName", "ALTER TABLE TestResults ADD COLUMN ScenarioName TEXT NULL");
            AddColumnIfMissing("Environment", "ALTER TABLE TestResults ADD COLUMN Environment TEXT NULL");
            AddColumnIfMissing("ApplicationName", "ALTER TABLE TestResults ADD COLUMN ApplicationName TEXT NULL");
        }
        catch (Exception) { /* Non-SQLite or table not created yet */ }
    }
}


app.Run();
