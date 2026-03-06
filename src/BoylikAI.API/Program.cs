using AspNetCoreRateLimit;
using BoylikAI.Infrastructure.Persistence;
using BoylikAI.API.Middleware;
using BoylikAI.Application;
using BoylikAI.Infrastructure;
using BoylikAI.Infrastructure.BackgroundJobs;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────-
builder.Host.UseSerilog((ctx, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Seq(ctx.Configuration["Seq:Url"] ?? "http://seq:5341"));

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
var otlpEndpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://otel-collector:4317";
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("BoylikAI.API"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("BoylikAI.API"))
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

// ── Application & Infrastructure ─────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, includeHangfireServer: true);
builder.Services.AddTelegramInfrastructure(builder.Configuration);

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BoylikAI Finance API", Version = "v1" });
    c.EnableAnnotations();
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
});

// ── JWT Authentication ────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });
builder.Services.AddAuthorization();

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres")
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis");

// ── CORS — restrict to known origins in non-development ───────────────────────
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            p.WithOrigins(
                    builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                    ?? [])
             .AllowAnyMethod()
             .AllowAnyHeader();
    });
});

var app = builder.Build();

// ── Database Migrations ───────────────────────────────────────────────────────
// Controlled via APPLY_MIGRATIONS env var. Always true in dev; false in prod
// (prod migrations run in CI/CD via dotnet ef database update).
if (app.Configuration.GetValue<bool>("ApplyMigrations") || app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSerilogRequestLogging(o =>
    o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms");

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BoylikAI API v1"));
}

app.UseIpRateLimiting();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// Protect /metrics from public access — only allow internal/localhost scraping
app.MapPrometheusScrapingEndpoint("/metrics")
    .RequireHost("localhost", "prometheus", "127.0.0.1");

// Hangfire dashboard — read-only in production, require auth
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    IsReadOnlyFunc = _ => !app.Environment.IsDevelopment(),
    Authorization = app.Environment.IsDevelopment()
        ? [new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter()]
        : [new BoylikAI.API.Infrastructure.HangfireAuthFilter()]
});

// ── Recurring Jobs ────────────────────────────────────────────────────────────
RecurringJob.AddOrUpdate<DailyReportJob>(
    "daily-report",
    job => job.ExecuteAsync(CancellationToken.None),
    "0 16 * * *"); // 16:00 UTC = 21:00 Tashkent (UTC+5)

await app.RunAsync();
