using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AgentBot.Models;
using AgentBot.Security;
using AgentBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;

namespace AgentBot.Handlers
{
    public class CommandHandler
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CommandHandler> _logger;
        private readonly List<IToolFunction> _tools;
        private readonly IAliasService _aliasService;
        private readonly ICronTaskService _cronTaskService;
        private readonly AccessControlService _accessControl;

        private readonly Dictionary<string, Func<Message, Task<string>>> _commandHandlers;

        private readonly ConcurrentDictionary<string, int> _commandStats = new();
        private readonly ConcurrentDictionary<long, int> _userMessageCount = new();
        private readonly DateTime _startTime = DateTime.UtcNow;

        public CommandHandler(
            IServiceProvider serviceProvider,
            ILogger<CommandHandler> logger,
            IEnumerable<IToolFunction> tools,
            IAliasService aliasService,
            ICronTaskService cronTaskService,
            AccessControlService accessControl)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tools = tools?.ToList() ?? new List<IToolFunction>();
            _aliasService = aliasService ?? throw new ArgumentNullException(nameof(aliasService));
            _cronTaskService = cronTaskService ?? throw new ArgumentNullException(nameof(cronTaskService));
            _accessControl = accessControl ?? throw new ArgumentNullException(nameof(accessControl));


            _commandHandlers = new Dictionary<string, Func<Message, Task<string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/start"]       = HandleStartAsync,
                ["/help"]        = HandleHelpAsync,
                ["/about"]       = HandleAboutAsync,
                ["/status"]      = HandleStatusAsync,
                ["/whoami"]      = HandleWhoAmIAsync,
                ["/alias"]       = HandleAliasAsync,
                ["/deletealias"] = HandleDeleteAliasAsync,
                ["/listaliases"] = HandleListAliasesAsync,
                ["/cron"]        = HandleCronAsync,
                ["/listcrons"]   = HandleListCronsAsync,
                ["/deletecron"]  = HandleDeleteCronAsync,
                ["/register"]    = HandleRegisterAsync,
                ["/restart"]     = HandleRestartAsync
            };
        }

        private IBotProvider BotProvider => _serviceProvider.GetRequiredService<IBotProvider>();

        public void TrackMessage(long chatId)
        {
            _userMessageCount.AddOrUpdate(chatId, 1, (_, count) => count + 1);
        }

        public async Task<bool> HandleCommandAsync(Message message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Text))
                return false;

            string text = message.Text.Trim();
            if (!text.StartsWith("/"))
                return false;

            long chatId = message.Chat.Id;
            string command = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

            int atIndex = command.IndexOf('@');
            if (atIndex > 0)
                command = command[..atIndex];

            _logger.LogInformation("Команда {Command} от chatId={ChatId} ({Username})",
                command, chatId, message.From?.Username);

            _commandStats.AddOrUpdate(command, 1, (_, c) => c + 1);

            if (_commandHandlers.TryGetValue(command, out var handler))
            {
                try
                {
                    string response = await handler(message);
                    if (!string.IsNullOrWhiteSpace(response))
                        await BotProvider.SendMessageAsync(chatId, response);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при выполнении команды {Command}", command);
                    await BotProvider.SendMessageAsync(chatId,
                        "Произошла ошибка при обработке команды 😔\nПопробуйте позже или /help");
                    return true;
                }
            }

            _logger.LogWarning("Неизвестная команда: {Command} от chatId={ChatId}", command, chatId);
            await BotProvider.SendMessageAsync(chatId,
                "Неизвестная команда 🤔\nИспользуйте /help для списка доступных команд.");
            return false;
        }

        private static string ExtractArgument(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText)) return string.Empty;
            int spaceIndex = messageText.IndexOf(' ');
            return spaceIndex > 0 ? messageText[(spaceIndex + 1)..].Trim() : string.Empty;
        }

        // ────────────────────────────────────────────────
        //  Базовые команды
        // ────────────────────────────────────────────────

        private Task<string> HandleStartAsync(Message message)
        {
            string username = message.From?.FirstName ?? message.From?.Username ?? "путешественник";
            return Task.FromResult(
                $"Привет, {username}! 👋\n" +
                "Я умный бот с ИИ-агентом и системой алиасов.\n" +
                "Пиши мне любые вопросы — постараюсь помочь.\n\n" +
                "Доступные команды:\n" +
                "/help — показать список команд\n" +
                "/alias — управлять алиасами\n" +
                "/listaliases — показать мои алиасы\n" +
                "/weather <город> — узнать погоду\n" +
                "/note <текст> — сохранить заметку\n" +
                "/about — о боте\n" +
                "/status — текущее состояние");
        }

        private Task<string> HandleHelpAsync(Message message) => Task.FromResult(
            "📋 Доступные команды:\n\n" +
            "🔹 /start — начать общение\n" +
            "🔹 /help — показать справку\n" +
            "🔹 /about — информация о боте\n" +
            "🔹 /status — проверить состояние\n\n" +
            "📚 Алиасы:\n" +
            "  /alias <имя> <значение> [type] — создать алиас\n" +
            "  /deletealias <имя> — удалить алиас\n" +
            "  /listaliases — показать все алиасы\n\n" +
            "🌤 /weather <город> — погода\n" +
            "📝 /note <текст> — сохранить заметку\n\n" +
            "Просто пиши вопросы — отвечу через ИИ-агент ✨");

        private Task<string> HandleAboutAsync(Message message) => Task.FromResult(
            "🤖 Этот бот создан на .NET 9 (Worker Service)\n" +
            "• Telegram API — Telegram.Bot\n" +
            "• ИИ — Google Gemini / OpenAI / Grok\n" +
            "• Инструменты: погода, заметки, Linux-команды\n" +
            "• Алиасы: персональная база знаний для ИИ\n" +
            "• Работает как systemd-сервис на Linux\n\n" +
            "Разработано для экспериментов и удовольствия 😄");

        private Task<string> HandleStatusAsync(Message message)
        {
            var now = DateTime.UtcNow;
            var uptime = now - _startTime;
            return Task.FromResult(
                $"🟢 Бот онлайн\n" +
                $"Время сервера: {now:yyyy-MM-dd HH:mm:ss} UTC\n" +
                $"Аптайм: {uptime.Days}д {uptime.Hours}ч {uptime.Minutes}м\n" +
                $"Версия: 2.0 (Alias + Knowledge Base)\n" +
                $"Инструментов: {_tools.Count} шт.\n" +
                $"Всё работает как надо 😉");
        }

        private Task<string> HandleWhoAmIAsync(Message message)
        {
            long chatId = message.Chat.Id;
            string username = message.From?.Username != null ? $"@{message.From.Username}" : "—";
            string role = _accessControl.IsAdmin(chatId) ? "👑 Администратор" : "👤 Пользователь";
            return Task.FromResult(
                $"🪪 Ваша информация:\n\n" +
                $"Chat ID: `{chatId}`\n" +
                $"Username: {username}\n" +
                $"Имя: {message.From?.FirstName} {message.From?.LastName}\n" +
                $"Роль: {role}");
        }

        private async Task<string> HandleRegisterAsync(Message message)
        {
            long chatId = message.Chat.Id;
            string password = ExtractArgument(message.Text!);

            if (string.IsNullOrWhiteSpace(password))
            {
                return "🔑 Регистрация администратора:\n\n" +
                       "Использование: /register <пароль>\n\n" +
                       "Введите пароль администратора для получения расширенных прав.\n" +
                       "После успешной регистрации вы сможете:\n" +
                       "• Выполнять команды с sudo\n" +
                       "• Работать с произвольными путями в системе\n" +
                       "• Управлять сервисами и процессами";
            }

            var result = await _accessControl.TryRegisterAdminAsync(chatId, password);
            return result switch
            {
                RegisterResult.Success => "✅ Вы успешно зарегистрированы как администратор!\n" +
                                          "Теперь вы можете выполнять команды с повышенными привилегиями.",
                RegisterResult.AlreadyAdmin => "⚠️ Вы уже являетесь администратором.",
                RegisterResult.WrongPassword => "❌ Неверный пароль администратора.",
                _ => "❌ Ошибка при регистрации."
            };
        }

        private async Task<string> HandleRestartAsync(Message message)
        {
            long chatId = message.Chat.Id;

            // Проверка прав администратора
            if (!_accessControl.IsAdmin(chatId))
            {
                return "❌ Доступ запрещён. Команда /restart доступна только администраторам.\n" +
                       "Используйте /register для получения прав администратора.";
            }

            // Запускаем перезагрузку в фоне
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                try
                {
                    // Попытка перезапуска через systemctl (для Linux)
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "systemctl",
                        Arguments = "restart agentbot",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode == 0)
                        {
                            _logger.LogInformation("Бот перезапущен через systemctl");
                            return;
                        }
                    }
                }
                catch
                {
                    // Если systemctl недоступен, просто завершаем процесс
                    // systemd или docker автоматически перезапустит контейнер
                }

                // Аварийное завершение процесса
                _logger.LogWarning("Перезагрузка через завершение процесса");
                Environment.Exit(0);
            });

            return "🔄 Перезагрузка бота...\n\n" +
                   "Бот будет перезапущен в течение нескольких секунд.\n" +
                   "Если бот не запустился автоматически — проверьте логи.";
        }

        // ────────────────────────────────────────────────
        //  Команды алиасов
        // ────────────────────────────────────────────────

        private async Task<string> HandleAliasAsync(Message message)
        {
            long chatId = message.Chat.Id;
            string args = ExtractArgument(message.Text!);

            if (string.IsNullOrWhiteSpace(args))
            {
                return "📚 Управление алиасами:\n\n" +
                       "Создать алиас команды:\n" +
                       "  /alias погода /weather\n" +
                       "  /alias заметки /note\n\n" +
                       "Создать алиас знания:\n" +
                       "  /alias cymmes это приложение blazortool knowledge\n\n" +
                       "Удалить алиас:\n" +
                       "  /deletealias погода\n\n" +
                       "Показать все алиасы:\n" +
                       "  /listaliases";
            }

            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return "⚠️ Формат: /alias <имя> <значение> [type]\n" +
                       "type: command (по умолчанию) или knowledge\n" +
                       "Пример: /alias погода /weather";
            }

            string aliasName = parts[0];
            string value = string.Join(' ', parts[1..]);
            AliasType type = AliasType.Command;

            // Проверяем, не указан ли тип в конце
            if (parts.Length >= 3)
            {
                string lastPart = parts[^1].ToLowerInvariant();
                if (lastPart == "knowledge" || lastPart == "know")
                {
                    type = AliasType.Knowledge;
                    value = string.Join(' ', parts[1..^1]);
                }
                else if (lastPart == "command" || lastPart == "cmd")
                {
                    type = AliasType.Command;
                    value = string.Join(' ', parts[1..^1]);
                }
            }

            try
            {
                var alias = await _aliasService.AddAliasAsync(chatId, aliasName, value, type);
                string typeEmoji = type == AliasType.Command ? "🔹" : "📖";
                return $"{typeEmoji} Алиас создан:\n\"{alias.AliasName}\" → \"{alias.Value}\"";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании алиаса {Alias} для chatId={ChatId}", aliasName, chatId);
                return "❌ Ошибка при создании алиаса. Возможно, он уже существует.";
            }
        }

        private async Task<string> HandleDeleteAliasAsync(Message message)
        {
            long chatId = message.Chat.Id;
            string aliasName = ExtractArgument(message.Text!);

            if (string.IsNullOrWhiteSpace(aliasName))
            {
                return "⚠️ Использование: /deletealias <имя>\n" +
                       "Пример: /deletealias погода";
            }

            bool deleted = await _aliasService.DeleteAliasAsync(chatId, aliasName);
            return deleted
                ? $"🗑 Алиас \"{aliasName}\" удалён."
                : $"⚠️ Алиас \"{aliasName}\" не найден.";
        }

        private async Task<string> HandleListAliasesAsync(Message message)
        {
            long chatId = message.Chat.Id;
            var allAliases = await _aliasService.GetAllAliasesAsync(chatId);

            if (!allAliases.Any())
            {
                return "📚 У вас пока нет алиасов.\n" +
                       "Создайте первый: /alias погода /weather";
            }

            var commandAliases = allAliases.Where(a => a.Type == AliasType.Command).ToList();
            var knowledgeAliases = allAliases.Where(a => a.Type == AliasType.Knowledge).ToList();

            var response = new System.Text.StringBuilder();
            response.AppendLine("📚 Ваши алиасы:");

            if (commandAliases.Any())
            {
                response.AppendLine("\n🔹 Команды:");
                foreach (var alias in commandAliases)
                {
                    response.AppendLine($"  {alias.AliasName} → {alias.Value}");
                }
            }

            if (knowledgeAliases.Any())
            {
                response.AppendLine("\n📖 Знания:");
                foreach (var alias in knowledgeAliases)
                {
                    response.AppendLine($"  {alias.AliasName} — {alias.Value}");
                }
            }

            return response.ToString();
        }

        // ────────────────────────────────────────────────
        //  Cron задачи
        // ────────────────────────────────────────────────

        private async Task<string> HandleCronAsync(Message message)
        {
            long chatId = message.Chat.Id;
            string args = ExtractArgument(message.Text!);

            if (string.IsNullOrWhiteSpace(args))
            {
                return "⏰ Управление Cron-задачами:\n\n" +
                       "Создать задачу:\n" +
                       "  /cron <название> <cron> <описание>\n\n" +
                       "Примеры cron:\n" +
                       "  0 10 * * * — каждый день в 10:00\n" +
                       "  */5 * * * * — каждые 5 минут\n" +
                       "  0 9 * * 1 — каждый понедельник в 9:00\n\n" +
                       "Пример: /cron morning \"0 8 * * *\" Отправить доброе утро";
            }

            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return "⚠️ Формат: /cron <название> <cron-expression> <описание>\n" +
                       "Пример: /cron morning \"0 8 * * *\" Отправить доброе утро";
            }

            string name = parts[0];
            string cronExpr = parts[1];
            string description = string.Join(' ', parts[2..]);

            // Проверяем валидность cron-выражения
            var nextRun = _cronTaskService.GetNextOccurrence(cronExpr);
            if (nextRun == null)
            {
                return "⚠️ Неверное cron-выражение. Используйте формат: минута час день месяц день_недели\n" +
                       "Пример: 0 10 * * *";
            }

            try
            {
                var task = await _cronTaskService.CreateTaskAsync(chatId, name, description, cronExpr);
                return $"⏰ Задача создана:\n" +
                       $"📌 Название: {task.Name}\n" +
                       $"⏱ Расписание: {task.CronExpression}\n" +
                       $"📝 Описание: {task.Description}\n" +
                       $"📅 Следующее выполнение: {nextRun:yyyy-MM-dd HH:mm:ss} UTC";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании cron-задачи {Name} для chatId={ChatId}", name, chatId);
                return "❌ Ошибка при создании задачи.";
            }
        }

        private async Task<string> HandleListCronsAsync(Message message)
        {
            long chatId = message.Chat.Id;
            var tasks = await _cronTaskService.GetAllTasksAsync(chatId);

            if (!tasks.Any())
            {
                return "⏰ У вас пока нет Cron-задач.\n" +
                       "Создайте первую: /cron morning \"0 8 * * *\" Доброе утро";
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine("⏰ Ваши задачи:");

            foreach (var task in tasks)
            {
                string status = task.IsActive ? "🟢" : "🔴";
                response.AppendLine($"\n{status} #{task.Id} {task.Name}");
                response.AppendLine($"   Расписание: {task.CronExpression}");
                response.AppendLine($"   Описание: {task.Description}");
                if (task.NextRun.HasValue)
                {
                    response.AppendLine($"   Следующее: {task.NextRun:yyyy-MM-dd HH:mm:ss} UTC");
                }
            }

            return response.ToString();
        }

        private async Task<string> HandleDeleteCronAsync(Message message)
        {
            long chatId = message.Chat.Id;
            string args = ExtractArgument(message.Text!);

            if (string.IsNullOrWhiteSpace(args))
            {
                return "⚠️ Использование: /deletecron <ID>\n" +
                       "Пример: /deletecron 5";
            }

            if (!long.TryParse(args, out var taskId))
            {
                return "⚠️ ID должен быть числом.";
            }

            bool deleted = await _cronTaskService.DeleteTaskAsync(chatId, taskId);
            return deleted
                ? $"🗑 Задача #{taskId} удалена."
                : $"⚠️ Задача #{taskId} не найдена.";
        }
    }
}
