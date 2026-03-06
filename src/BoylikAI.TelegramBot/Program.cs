using BoylikAI.Application;
using BoylikAI.Infrastructure;
using BoylikAI.TelegramBot.Handlers;
using BoylikAI.TelegramBot.Services;
using BoylikAI.TelegramBot.Workers;
using BoylikAI.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Telegram.Bot;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, lc) => lc
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Seq(ctx.Configuration["Seq:Url"] ?? "http://seq:5341"))
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // Telegram Bot Client
        var botToken = config["Telegram:BotToken"]
            ?? throw new InvalidOperationException("Telegram:BotToken is required");

        services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));

        // Application & Infrastructure
        // includeHangfireServer: false — the API process owns the Hangfire server.
        // The bot worker must NOT run a second server or jobs execute twice.
        services.AddApplication();
        services.AddInfrastructure(config, includeHangfireServer: false);

        // Bot-specific notification service (overrides Infrastructure's TelegramNotificationService
        // since the bot project owns the ITelegramBotClient singleton here)
        services.AddScoped<INotificationService, TelegramBotService>();

        // Bot handlers
        services.AddScoped<MessageHandler>();
        services.AddScoped<CallbackQueryHandler>();

        // Background worker
        services.AddHostedService<BotPollingWorker>();
    })
    .Build();

await host.RunAsync();
