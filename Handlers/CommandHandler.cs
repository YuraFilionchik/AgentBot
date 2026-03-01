using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace AgentBot.Handlers
{
    internal class CommandHandler
    {
        private readonly IBotProvider _botProvider; // для отправки ответов
        private readonly ILogger<CommandHandler> _logger;

        // Можно расширять: словарь команд → обработчик или отдельные методы
        private readonly Dictionary<string, Func<Message, Task<string>>> _commandHandlers;

        public CommandHandler(
            IBotProvider botProvider,
            ILogger<CommandHandler> logger)
        {
            _botProvider = botProvider ?? throw new ArgumentNullException(nameof(botProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _commandHandlers = new Dictionary<string, Func<Message, Task<string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/start"] = HandleStartAsync,
                ["/help"] = HandleHelpAsync,
                ["/about"] = HandleAboutAsync,
                ["/status"] = HandleStatusAsync,
                // Добавляйте сюда новые команды по мере необходимости
            };
        }

        /// <summary>
        /// Основной метод обработки входящей команды.
        /// Вызывается из MessageProcessor, если сообщение начинается с '/'.
        /// </summary>
        /// <param name="message">Сообщение от Telegram</param>
        /// <returns>true — если команда была обработана, false — если команда неизвестна</returns>
        public async Task<bool> HandleCommandAsync(Message message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Text))
                return false;

            string text = message.Text.Trim();
            if (!text.StartsWith("/"))
                return false;

            // Извлекаем команду (до пробела или конца строки)
            string command = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

            _logger.LogInformation("Получена команда {Command} от пользователя {UserId} ({Username}) в чате {ChatId}",
                command, message.From?.Id, message.From?.Username, message.Chat.Id);

            if (_commandHandlers.TryGetValue(command, out var handler))
            {
                try
                {
                    string response = await handler(message);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        await _botProvider.SendMessageAsync(message.Chat.Id, response);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при выполнении команды {Command}", command);
                    await _botProvider.SendMessageAsync(message.Chat.Id,
                        "Произошла ошибка при обработке команды 😔\nПопробуйте позже или /help");
                    return true; // команда была распознана, даже если ошибка
                }
            }

            // Неизвестная команда
            _logger.LogWarning("Неизвестная команда: {Command} от {UserId}", command, message.From?.Id);
            await _botProvider.SendMessageAsync(message.Chat.Id,
                "Неизвестная команда 🤔\nИспользуйте /help для списка доступных команд.");

            return false;
        }

        // ────────────────────────────────────────────────
        //                  Реализации команд
        // ────────────────────────────────────────────────

        private Task<string> HandleStartAsync(Message message)
        {
            string username = message.From?.FirstName ?? message.From?.Username ?? "путешественник";
            return Task.FromResult(
                $"Привет, {username}! 👋\n" +
                "Я умный бот с ИИ-агентом на базе Gemini.\n" +
                "Пиши мне любые вопросы — я постараюсь помочь.\n\n" +
                "Доступные команды:\n" +
                "/help — показать этот список\n" +
                "/about — о боте\n" +
                "/status — текущее состояние");
        }

        private Task<string> HandleHelpAsync(Message message)
        {
            return Task.FromResult(
                "📋 Доступные команды:\n\n" +
                "/start — начать общение / перезапустить\n" +
                "/help — показать справку\n" +
                "/about — информация о боте\n" +
                "/status — проверить, жив ли я\n\n" +
                "Просто пиши любые вопросы — я передам их ИИ-агенту ✨");
        }

        private Task<string> HandleAboutAsync(Message message)
        {
            return Task.FromResult(
                "🤖 Этот бот создан на .NET 8/9 (Worker Service)\n" +
                "• Telegram API — Telegram.Bot\n" +
                "• ИИ — Google Gemini (через Google.GenAI)\n" +
                "• Поддерживает команды и свободный диалог\n" +
                "• Работает как systemd-сервис на Linux\n\n" +
                "Разработано для экспериментов и удовольствия 😄");
        }

        private Task<string> HandleStatusAsync(Message message)
        {
            var now = DateTime.UtcNow;
            return Task.FromResult(
                $"🟢 Бот онлайн\n" +
                $"Время сервера: {now:yyyy-MM-dd HH:mm:ss} UTC\n" +
                $"Версия: 1.0 (Gemini edition)\n" +
                $"Обработка: команды + ИИ-агент\n" +
                $"Всё работает как надо 😉");
        }

        // Место для будущих команд
    }
}
