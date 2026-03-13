using Anthropic.SDK;
using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Domain.Interfaces;
using BoylikAI.Infrastructure.AI;
using BoylikAI.Infrastructure.Analytics;
using BoylikAI.Infrastructure.Caching;
using BoylikAI.Infrastructure.Export;
using BoylikAI.Infrastructure.Messaging;
using BoylikAI.Infrastructure.Persistence;
using BoylikAI.Infrastructure.Persistence.Repositories;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Telegram.Bot;

namespace BoylikAI.Infrastructure;

public static class DependencyInjection
{
    /// <param name="includeHangfireServer">
    /// Pass <c>false</c> in the TelegramBot worker host — only the API process
    /// should run the Hangfire server to avoid duplicate job execution.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeHangfireServer = true)
    {
        // ── PostgreSQL via EF Core ───────────────────────────────────────────
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                o => o.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null)
                      .CommandTimeout(30)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // ── Repositories ─────────────────────────────────────────────────────
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();

        // ── Redis ────────────────────────────────────────────────────────────
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var connStr = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");

            var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
            var config = ConfigurationOptions.Parse(connStr);
            config.AbortOnConnectFail = false; // Graceful degradation on startup
            var mux = ConnectionMultiplexer.Connect(config);

            mux.ConnectionFailed += (_, e) =>
                logger.LogWarning("Redis connection failed: {FailureType}", e.FailureType);
            mux.ConnectionRestored += (_, _) =>
                logger.LogInformation("Redis connection restored");

            return mux;
        });
        services.AddScoped<ICacheService, RedisCacheService>();

        // ── Claude AI ────────────────────────────────────────────────────────
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));

        var anthropicKey = configuration[$"{AnthropicOptions.SectionName}:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is required");
        services.AddSingleton(new AnthropicClient(anthropicKey));

        services.AddSingleton<RuleBasedCategoryClassifier>();
        services.AddScoped<ITransactionParser, ClaudeTransactionParser>();
        services.AddScoped<IAdviceGenerator, ClaudeAdviceGenerator>();
        services.AddScoped<IChatService, ClaudeChatService>();

        // ── Export ───────────────────────────────────────────────────────────
        services.AddScoped<IExcelExportService, ExcelExportService>();

        // ── Analytics ────────────────────────────────────────────────────────
        services.AddScoped<IAnalyticsEngine, AnalyticsEngine>();

        // ── Hangfire ─────────────────────────────────────────────────────────
        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(configuration.GetConnectionString("Postgres")!));

        if (includeHangfireServer)
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = 5;
                options.Queues = ["critical", "default", "low"];
            });
        }

        return services;
    }

    /// <summary>
    /// Registers ITelegramBotClient + TelegramNotificationService.
    /// Called in both API and TelegramBot hosts.
    /// </summary>
    public static IServiceCollection AddTelegramInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var botToken = configuration["Telegram:BotToken"]
            ?? throw new InvalidOperationException("Telegram:BotToken is required");

        services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
        services.AddScoped<INotificationService, TelegramNotificationService>();

        return services;
    }
}
