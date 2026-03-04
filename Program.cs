using AgentBot;
using AgentBot.AiAgents;
using AgentBot.Bots;
using AgentBot.Handlers;
using AgentBot.Memory;
using AgentBot.Models;
using AgentBot.Security;
using AgentBot.Services;
using AgentBot.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Конфигурация: корневой appsettings.json загружается автоматически через CreateApplicationBuilder.
// Config/appsettings.json — секреты (токены, ключи). Путь от директории приложения, не от WorkingDirectory.
var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);


// Логи (Serilog только в файл)
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger());

// ────────────────────────────────────────────────
// Регистрация сервисов (DI)
// ────────────────────────────────────────────────

// Память/хранилище истории чатов
builder.Services.AddSingleton<IConversationMemory, InMemoryConversationStorage>();

// Сервис алиасов и базы знаний
builder.Services.AddSingleton<IAliasService, SQLiteAliasService>();

// Сервис Cron-задач
builder.Services.AddSingleton<ICronTaskService, SQLiteCronTaskService>();

// Сервис клавиатур
builder.Services.AddSingleton<IKeyboardService, SQLiteKeyboardService>();

// LLM Wrapper для формирования контекста
builder.Services.AddSingleton<ILlmWrapper, LlmWrapper>();

// Боты и ИИ-агенты
builder.Services.AddSingleton<IBotProvider, TelegramBotProvider>();
builder.Services.AddSingleton<IAiAgent, GeminiAiAgent>();

// Обработчики
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddSingleton<MessageProcessor>();

// Безопасность и контроль доступа
builder.Services.AddSingleton<AccessControlService>();

// Инструменты (Tools)
builder.Services.AddHttpClient();
builder.Services.AddTransient<IToolFunction, LinuxCMDTool>();
builder.Services.AddTransient<IToolFunction, WeatherTool>();
builder.Services.AddTransient<IToolFunction, SendMessageTool>();
builder.Services.AddTransient<IToolFunction, SendFileTool>();
// builder.Services.AddTransient<IToolFunction, DatabaseTool>(); // Когда будет реализован

// Фоновые сервисы
builder.Services.AddHostedService<BotWorker>();
builder.Services.AddHostedService<CronTaskRunner>(); // Cron-планировщик

// Systemd-интеграция (для Linux-демона)
builder.Services.AddSystemd();

var host = builder.Build();
await host.RunAsync();
