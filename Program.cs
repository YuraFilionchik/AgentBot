using AgentBot;
using AgentBot.AiAgents;
using AgentBot.Bots;
using AgentBot.Handlers;
using AgentBot.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Конфигурация (appsettings.json + env vars)
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Логи (Serilog в файл и консоль)
builder.Logging.AddSerilog(new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger());


// Регистрация сервисов (DI)
builder.Services.AddSingleton<IBotProvider, TelegramBotProvider>();
builder.Services.AddSingleton<IAiAgent, GeminiAiAgent>();
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddTransient<IToolFunction, LinuxCMDTool>();
builder.Services.AddHostedService<BotWorker>(); // Основной сервис
// Systemd-интеграция (для Linux-демона)
builder.Services.AddSystemd();

var host = builder.Build();
await host.RunAsync();
