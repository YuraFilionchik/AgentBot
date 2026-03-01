using AgentBot;
using AgentBot.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
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

        public Func<long, string, Task>? OnMessageReceived { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public TelegramBotProvider(IConfiguration configuration, MessageProcessor processor, ILogger<TelegramBotProvider> logger)
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
                AllowedUpdates = new[] { UpdateType.Message }
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling update for chat {ChatId}", update.Message?.Chat.Id);
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Telegram polling error occurred.");

            // Простой ретрай: задержка перед следующим попыткой (встроено в StartReceiving, но логируем)
            // Для продвинутых ретраев можно использовать Polly, но здесь базово
            return Task.CompletedTask; // Продолжаем polling
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
                // Ретрай: можно добавить Polly здесь для повторных попыток
            }
        }

        public Task StartPollingAsync()
        {
            throw new NotImplementedException();
        }

        public Task SendMessageAsync(long chatId, string text)
        {
            throw new NotImplementedException();
        }
    }
}