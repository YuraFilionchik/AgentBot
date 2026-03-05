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
                ["/restart"]     = HandleRestartAsync,
                ["/backup"]      = HandleBackupAsync,
                ["/restore"]     = HandleRestoreAsync,
                ["/update"]      = HandleUpdateAsync
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

        /// <summary>
        /// Parses a string into tokens, treating text in double quotes as a single token.
        /// Quotes are stripped from the resulting tokens.
        /// Example: morning "0 8 * * 3" Проверь статус → ["morning", "0 8 * * 3", "Проверь", "статус"]
        /// </summary>
        private static List<string> ParseQuotedArgs(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
                return result;

            var span = input.AsSpan().Trim();
            int i = 0;

            while (i < span.Length)
            {
                // Skip whitespace
                while (i < span.Length && char.IsWhiteSpace(span[i]))
                    i++;

                if (i >= span.Length)
                    break;

                if (span[i] == '"')
                {
                    // Quoted token — find closing quote
                    i++; // skip opening quote
                    int start = i;
                    while (i < span.Length && span[i] != '"')
                        i++;

                    result.Add(span[start..i].ToString());

                    if (i < span.Length)
                        i++; // skip closing quote
                }
                else
                {
                    // Unquoted token — read until whitespace
                    int start = i;
                    while (i < span.Length && !char.IsWhiteSpace(span[i]))
                        i++;

                    result.Add(span[start..i].ToString());
                }
            }

            return result;
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
            "🔧 Администрирование:\n" +
            "  /restart — перезапустить бота\n" +
            "  /update — обновить бота (git pull + rebuild)\n" +
            "  /backup — создать резервную копию\n" +
            "  /restore [файл] — восстановить из бекапа\n" +
            "  /register <пароль> — получить права админа\n\n" +
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

            if (!_accessControl.IsAdmin(chatId))
                return "❌ Доступ запрещён. Команда /restart доступна только администраторам.\n" +
                       "Используйте /register для получения прав администратора.";

            _logger.LogInformation("Chat {ChatId}: запуск перезагрузки бота", chatId);

            // Запускаем скрипт перезагрузки в фоне, чтобы успеть отправить ответ
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                try
                {
                    await RunScriptAsync("scripts/restart_agentbot.sh");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при перезапуске через скрипт, аварийное завершение");
                    Environment.Exit(0);
                }
            });

            return "🔄 Перезагрузка бота...\n" +
                   "Бот будет перезапущен через скрипт restart_agentbot.sh.";
        }

        private async Task<string> HandleBackupAsync(Message message)
        {
            long chatId = message.Chat.Id;

            if (!_accessControl.IsAdmin(chatId))
                return "❌ Доступ запрещён. Команда /backup доступна только администраторам.";

            _logger.LogInformation("Chat {ChatId}: создание бекапа", chatId);

            try
            {
                string output = await RunScriptAsync("scripts/backup_bot.sh", "make");
                return $"💾 Бекап создан:\n\n```\n{output}\n```";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании бекапа");
                return $"❌ Ошибка при создании бекапа: {ex.Message}";
            }
        }

        private async Task<string> HandleRestoreAsync(Message message)
        {
            long chatId = message.Chat.Id;

            if (!_accessControl.IsAdmin(chatId))
                return "❌ Доступ запрещён. Команда /restore доступна только администраторам.";

            string backupFile = ExtractArgument(message.Text!);
            _logger.LogInformation("Chat {ChatId}: восстановление из бекапа {File}", chatId,
                string.IsNullOrEmpty(backupFile) ? "(последний)" : backupFile);

            try
            {
                string args = string.IsNullOrWhiteSpace(backupFile) ? "restore" : $"restore {backupFile}";
                string output = await RunScriptAsync("scripts/backup_bot.sh", args);
                return $"✅ Восстановление завершено:\n\n```\n{output}\n```";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при восстановлении из бекапа");
                return $"❌ Ошибка при восстановлении: {ex.Message}";
            }
        }

        private async Task<string> HandleUpdateAsync(Message message)
        {
            long chatId = message.Chat.Id;

            if (!_accessControl.IsAdmin(chatId))
                return "❌ Доступ запрещён. Команда /update доступна только администраторам.";

            _logger.LogInformation("Chat {ChatId}: запуск обновления бота", chatId);

            // Обновление занимает время — уведомляем и запускаем в фоне
            _ = Task.Run(async () =>
            {
                try
                {
                    // update_agentbot.sh uses INITIAL_PWD=$(pwd) to locate backup_bot.sh,
                    // so we must cd into the scripts directory before running it
                    string output = await RunScriptAsync("cd scripts && bash update_agentbot.sh");
                    _logger.LogInformation("Обновление завершено: {Output}", output);
                    await BotProvider.SendMessageAsync(chatId, $"✅ Обновление завершено:\n\n```\n{output}\n```");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обновлении бота");
                    try
                    {
                        await BotProvider.SendMessageAsync(chatId, $"❌ Ошибка при обновлении: {ex.Message}");
                    }
                    catch { /* бот мог уже перезапуститься */ }
                }
            });

            return "🔄 Обновление запущено...\n" +
                   "Будет создан бекап, затем pull + rebuild + restart.\n" +
                   "Результат придёт отдельным сообщением.";
        }

        // ────────────────────────────────────────────────
        //  Shell script runner
        // ────────────────────────────────────────────────

        private async Task<string> RunScriptAsync(string scriptPath, string? arguments = null, string? stdinText = null)
        {
            string command = string.IsNullOrWhiteSpace(arguments)
                ? scriptPath
                : $"{scriptPath} {arguments}";

            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinText is not null,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };
            processInfo.ArgumentList.Add("-c");
            processInfo.ArgumentList.Add(command);

            using var process = new Process { StartInfo = processInfo };
            process.Start();

            if (stdinText is not null)
            {
                await process.StandardInput.WriteLineAsync(stdinText);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Script failed (exit {process.ExitCode}): {error.Trim()}");

            return output.Trim();
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
                       "  /cron <название> \"<cron>\" <описание>\n\n" +
                       "Примеры cron:\n" +
                       "  \"0 10 * * *\" — каждый день в 10:00\n" +
                       "  \"*/5 * * * *\" — каждые 5 минут\n" +
                       "  \"0 9 * * 1\" — каждый понедельник в 9:00\n\n" +
                       "Пример: /cron morning \"0 8 * * *\" Отправить доброе утро";
            }

            // Парсим аргументы с учётом кавычек: /cron <name> "<cron>" <description>
            var parsedParts = ParseQuotedArgs(args);
            if (parsedParts.Count < 3)
            {
                return "⚠️ Формат: /cron <название> \"<cron-expression>\" <описание>\n" +
                       "Пример: /cron morning \"0 8 * * *\" Отправить доброе утро";
            }

            string name = parsedParts[0];
            string cronExpr = parsedParts[1];
            string description = string.Join(' ', parsedParts.Skip(2));

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
