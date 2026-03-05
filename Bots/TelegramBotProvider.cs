using AgentBot.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AgentBot.Bots
{
    public class TelegramBotProvider : IBotProvider
    {
        private readonly TelegramBotClient _client;
        private readonly MessageProcessor _processor;
        private readonly ILogger<TelegramBotProvider> _logger;

        public Func<long, string, Task>? OnMessageReceived { get; set; }

        public TelegramBotProvider(
            IConfiguration configuration,
            MessageProcessor processor,
            ILogger<TelegramBotProvider> logger)
        {
            var token = configuration["Bots:Telegram:ApiToken"];
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Telegram API token is not configured.");
            }

            _client = new TelegramBotClient(token);
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartPollingAsync(CancellationToken cancellationToken = default)
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            _logger.LogInformation("Telegram polling started.");
            var me = await _client.GetMe(cancellationToken);
            _logger.LogInformation("Bot connected: {BotName}", me.Username);

            _client.StartReceiving(
                updateHandler: (botClient, update, token) => HandleUpdateAsync(botClient, update, token),
                errorHandler: (botClient, exception, errorSource, token) => HandleErrorAsync(botClient, exception, errorSource, token),
                receiverOptions: receiverOptions,
                cancellationToken: cancellationToken);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Message != null && update.Message.Text != null)
                {
                    _logger.LogDebug("Received message from chat {ChatId}: {MessageText}", update.Message.Chat.Id, update.Message.Text);
                    await _processor.ProcessAsync(update.Message);
                }
                else if (update.CallbackQuery != null)
                {
                    // Обработка inline-кнопок
                    _logger.LogDebug("Received callback query from chat {ChatId}: {Data}", update.CallbackQuery.From.Id, update.CallbackQuery.Data);
                    await _processor.HandleCallbackAsync(update.CallbackQuery);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling update for chat {ChatId}", update.Message?.Chat.Id);
            }
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource errorSource, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Telegram error from {ErrorSource}", errorSource);
            return Task.CompletedTask;
        }

        public async Task SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup = null, ParseMode? parseMode = null, CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.SendMessage(
                    chatId: new ChatId(chatId),
                    text: text,
                    replyMarkup: replyMarkup,
                    parseMode: parseMode ?? default,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Sent message to chat {ChatId}: {MessageText}", chatId, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to chat {ChatId}", chatId);
            }
        }

        public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.AnswerCallbackQuery(
                    callbackQueryId: callbackQueryId,
                    text: text,
                    showAlert: showAlert,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error answering callback query {CallbackId}", callbackQueryId);
            }
        }

        public async Task SendFileAsync(long chatId, byte[] fileContent, string fileName, string? caption = null, CancellationToken cancellationToken = default)
        {
            try
            {
                using var stream = new MemoryStream(fileContent);
                var file = InputFile.FromStream(stream, fileName);

                await _client.SendDocument(
                    chatId: new ChatId(chatId),
                    document: file,
                    caption: caption,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Sent file {FileName} to chat {ChatId}", fileName, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending file {FileName} to chat {ChatId}", fileName, chatId);
            }
        }

        public async Task SendFileFromPathAsync(long chatId, string filePath, string? caption = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                await using var stream = File.OpenRead(filePath);
                var fileName = Path.GetFileName(filePath);
                var file = InputFile.FromStream(stream, fileName);

                await _client.SendDocument(
                    chatId: new ChatId(chatId),
                    document: file,
                    caption: caption,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Sent file {FileName} to chat {ChatId}", fileName, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending file {FilePath} to chat {ChatId}", filePath, chatId);
            }
        }
    }
}
