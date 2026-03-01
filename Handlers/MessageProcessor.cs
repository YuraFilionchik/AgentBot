using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace AgentBot.Handlers
{
    /// <summary>
    /// Процессор сообщений: маршрутизирует входящие сообщения
    /// между CommandHandler (для команд) и IAiAgent (для обычных сообщений).
    /// </summary>
    internal class MessageProcessor
    {
        private readonly CommandHandler _commandHandler;
        private readonly IAiAgent _aiAgent;
        private readonly IBotProvider _botProvider;
        private readonly ILogger<MessageProcessor> _logger;
        private readonly List<IToolFunction> _tools;

        public MessageProcessor(
            CommandHandler commandHandler,
            IAiAgent aiAgent,
            IBotProvider botProvider,
            ILogger<MessageProcessor> logger,
            IEnumerable<IToolFunction> tools)
        {
            _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            _aiAgent = aiAgent ?? throw new ArgumentNullException(nameof(aiAgent));
            _botProvider = botProvider ?? throw new ArgumentNullException(nameof(botProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tools = tools?.ToList() ?? new List<IToolFunction>();
        }

        public async Task ProcessAsync(Message message)
        {
            if (message == null || message.Text == null)
            {
                _logger.LogWarning("Получено пустое сообщение или без текста");
                return;
            }

            long chatId = message.Chat.Id;
            string text = message.Text.Trim();

            _logger.LogDebug("Chat {ChatId}: получено сообщение: {Text}", chatId, text);

            // Отслеживаем сообщение для статистики
            _commandHandler.TrackMessage(chatId);

            try
            {
                // Если сообщение начинается с "/", обрабатываем как команду
                if (text.StartsWith("/"))
                {
                    _logger.LogInformation("Chat {ChatId}: обработка команды", chatId);
                    if (await _commandHandler.HandleCommandAsync(message))
                    {
                        return;
                    }
                }

                // Обрабатываем как обычное сообщение через ИИ-агент
                _logger.LogInformation("Chat {ChatId}: отправка сообщения ИИ-агенту", chatId);
                string response = await _aiAgent.ProcessMessageAsync(chatId, text, _tools);

                if (!string.IsNullOrWhiteSpace(response))
                {
                    await _botProvider.SendMessageAsync(chatId, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat {ChatId}: ошибка при обработке сообщения", chatId);
                await _botProvider.SendMessageAsync(chatId,
                    "Произошла ошибка при обработке сообщения 😔\nПопробуйте позже.");
            }
        }

        /// <summary>
        /// Обработка callback-запросов от inline-кнопок.
        /// </summary>
        public async Task HandleCallbackAsync(CallbackQuery callbackQuery)
        {
            try
            {
                long chatId = callbackQuery.From.Id;
                string data = callbackQuery.Data ?? string.Empty;

                _logger.LogDebug("Chat {ChatId}: получен callback: {Data}", chatId, data);

                // Здесь будет логика обработки inline-кнопок
                // Пока просто подтверждаем получение
                await _botProvider.SendMessageAsync(chatId, $"Получена команда: {data}");

                // Подтверждаем callback
                // await _botProvider.AnswerCallbackQueryAsync(callbackQuery.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке callback");
            }
        }
    }
}
