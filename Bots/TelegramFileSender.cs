using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AgentBot.Bots
{
    /// <summary>
    /// Расширенный интерфейс для отправки файлов.
    /// </summary>
    public interface IFileSender
    {
        /// <summary>
        /// Отправляет файл пользователю.
        /// </summary>
        Task SendFileAsync(long chatId, byte[] fileContent, string fileName, string? caption = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Отправляет файл из файловой системы.
        /// </summary>
        Task SendFileFromPathAsync(long chatId, string filePath, string? caption = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Реализация отправки файлов через Telegram Bot API.
    /// </summary>
    public class TelegramFileSender : IFileSender
    {
        private readonly TelegramBotClient _client;
        private readonly ILogger<TelegramFileSender> _logger;

        public TelegramFileSender(
            IConfiguration configuration,
            ILogger<TelegramFileSender> logger)
        {
            var token = configuration["Bots:Telegram:ApiToken"];
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Telegram API token is not configured.");
            }

            _client = new TelegramBotClient(token);
            _logger = logger;
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
                throw;
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
                throw;
            }
        }
    }
}
