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

// Run Database Migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UiTestRunner.Data.ApplicationDbContext>();
    // Replaces EnsureCreated() to allow incremental schema updates without dropping old data
    db.Database.Migrate();

    // Fallback: ensure columns exist if an older DB predates migrations. Use raw connection so we only ALTER when missing (avoids EF logging "fail" for duplicate column).
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


app.Run();
