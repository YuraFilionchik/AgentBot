using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using AgentBot.Security;
using AgentBot.Services;

namespace AgentBot.Handlers
{
    /// <summary>
    /// Процессор сообщений: маршрутизирует входящие сообщения
    /// между CommandHandler (для команд) и IAiAgent (для обычных сообщений).
    /// </summary>
    public class MessageProcessor
    {
        private readonly CommandHandler _commandHandler;
        private readonly IAiAgent _aiAgent;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MessageProcessor> _logger;
        private readonly List<IToolFunction> _tools;
        private readonly IKeyboardService _keyboardService;
        private readonly AccessControlService _accessControl;

        public MessageProcessor(
            CommandHandler commandHandler,
            IAiAgent aiAgent,
            IServiceProvider serviceProvider,
            ILogger<MessageProcessor> logger,
            IEnumerable<IToolFunction> tools,
            IKeyboardService keyboardService,
            AccessControlService accessControl)
        {
            _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            _aiAgent = aiAgent ?? throw new ArgumentNullException(nameof(aiAgent));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tools = tools?.ToList() ?? new List<IToolFunction>();
            _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
            _accessControl = accessControl ?? throw new ArgumentNullException(nameof(accessControl));
        }

        private IBotProvider BotProvider => _serviceProvider.GetRequiredService<IBotProvider>();

        /// <summary>
        /// Возвращает инструменты, доступные пользователю чата.
        /// SendMessage/SendFile отдаются только администраторам — иначе любой пользователь
        /// может заставить бота отправлять сообщения/файлы куда угодно (спам/эксфильтрация).
        /// </summary>
        private List<IToolFunction> GetToolsForChat(long chatId)
        {
            if (_accessControl.IsAdmin(chatId))
                return _tools;

            return _tools.Where(t => t.Name is not ("SendMessage" or "SendFile")).ToList();
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
                // Проверяем, не является ли текст меткой кнопки
                var commandRef = await _keyboardService.TryGetCommandByLabelAsync(chatId, text);
                if (commandRef != null)
                {
                    _logger.LogInformation("Chat {ChatId}: текст '{Text}' распознан как кнопка -> {Command}", chatId, text, commandRef);
                    message.Text = commandRef;
                    text = commandRef;
                }

                // Доступ только для администраторов: незарегистрированным пользователям
                // разрешена только команда /register. Зарегистрированный пользователь = администратор.
                if (!_accessControl.IsAdmin(chatId))
                {
                    string firstToken = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                    int atIndex = firstToken.IndexOf('@');
                    if (atIndex > 0)
                        firstToken = firstToken[..atIndex];

                    if (!firstToken.Equals("/register", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Chat {ChatId}: доступ запрещён (не администратор)", chatId);
                        await BotProvider.SendMessageAsync(chatId,
                            "🔒 Доступ только для администраторов.\nИспользуйте /register <пароль> для входа.");
                        return;
                    }
                }

                // Если сообщение (или его замена) начинается с "/", обрабатываем как команду
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
                string response = await _aiAgent.ProcessMessageAsync(chatId, text, GetToolsForChat(chatId));

                if (!string.IsNullOrWhiteSpace(response))
                {
                    var keyboard = await _keyboardService.GetMainKeyboardAsync(chatId);
                    await BotProvider.SendMessageAsync(chatId, response, replyMarkup: keyboard);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat {ChatId}: ошибка при обработке сообщения", chatId);
                await BotProvider.SendMessageAsync(chatId,
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
                await BotProvider.SendMessageAsync(chatId, $"Получена команда: {data}");

                // Подтверждаем callback (убирает "крутилку" на кнопке)
                await BotProvider.AnswerCallbackQueryAsync(callbackQuery.Id, $"Вы выбрали: {data}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке callback");
            }
        }
    }
}
