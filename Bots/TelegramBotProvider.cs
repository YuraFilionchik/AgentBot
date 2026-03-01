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

            _client.StartReceiving(HandleUpdateAsync, HandlePollingErrorAsync, receiverOptions, cancellationToken);

            _logger.LogInformation("Telegram polling started.");
            var me = await _client.GetMe(cancellationToken);
            _logger.LogInformation("Bot connected: {BotName}", me.Username);
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

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Telegram polling error occurred.");
            return Task.CompletedTask;
        }

        public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.SendMessage(new ChatId(chatId), text, cancellationToken: cancellationToken);
                _logger.LogDebug("Sent message to chat {ChatId}: {MessageText}", chatId, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to chat {ChatId}", chatId);
            }
        }

        public async Task SendFileAsync(long chatId, byte[] fileContent, string fileName, string? caption = null, CancellationToken cancellationToken = default)
        {
            try
            {
                using var stream = new MemoryStream(fileContent);
                var file = new InputFile(stream, fileName);

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
                var file = new InputFile(stream, fileName);

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
