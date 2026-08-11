using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBot.Tools
{
    /// <summary>
    /// Инструмент для отправки файла пользователю.
    /// </summary>
    public class SendFileTool : IToolFunction
    {
        public string Name => "SendFile";

        public string Description =>
            "Отправить файл в текущий чат (Telegram). " +
            "Файл всегда отправляется в чат, откуда пришёл запрос, — указывать chat_id не нужно. " +
            "Файл может быть создан из содержимого (content) или прочитан из пути (file_path).";

        public Dictionary<string, string> Parameters => new()
        {
            { "file_name", "string" },    // Имя файла
            { "content", "string" },      // Содержимое файла (если создаётся на лету)
            { "file_path", "string" },    // Путь к файлу (если отправляется существующий)
            { "caption", "string" }       // Подпись к файлу (опционально)
        };

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SendFileTool> _logger;

        public SendFileTool(
            IServiceProvider serviceProvider,
            ILogger<SendFileTool> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        private IBotProvider BotProvider => _serviceProvider.GetRequiredService<IBotProvider>();

        private static string GetStringArg(object? obj)
        {
            if (obj == null) return string.Empty;
            if (obj is string s) return s;
            if (obj is JsonElement je) return je.ValueKind == JsonValueKind.Null ? string.Empty : (je.GetString() ?? string.Empty);
            return obj.ToString() ?? string.Empty;
        }


        public async Task<string> ExecuteAsync(Dictionary<string, object> args, long toolChatId = default)
        {
            try
            {
                // Безопасность: файл отправляется ТОЛЬКО в чат, который инициировал обработку
                // (серверный toolChatId). Переданный моделью chat_id игнорируется, чтобы исключить
                // отправку в произвольные чаты (эксфильтрация/спам).
                if (toolChatId <= 0)
                {
                    return JsonSerializer.Serialize(new { error = "Недоступен контекст чата для отправки файла." });
                }

                if (!args.TryGetValue("file_name", out var fileNameObj))
                {
                    return JsonSerializer.Serialize(new { error = "file_name обязателен" });
                }
                string fileName = GetStringArg(fileNameObj);
                if (string.IsNullOrEmpty(fileName))
                {
                    return JsonSerializer.Serialize(new { error = "file_name обязателен и не может быть пустым" });
                }

                string? caption = args.TryGetValue("caption", out var capObj) ? GetStringArg(capObj) : null;
                if (string.IsNullOrEmpty(caption)) caption = null;

                // Вариант 1: Отправка файла из содержимого
                if (args.TryGetValue("content", out var contentObj))
                {
                    string content = GetStringArg(contentObj);
                    if (!string.IsNullOrEmpty(content))
                    {
                        byte[] fileBytes = Encoding.UTF8.GetBytes(content);
                        _logger.LogInformation("Отправка файла {FileName} в чат {ChatId} (из содержимого)", fileName, toolChatId);
                        await BotProvider.SendFileAsync(toolChatId, fileBytes, fileName, caption);

                        return JsonSerializer.Serialize(new
                        {
                            success = true,
                            message = "Файл отправлен",
                            chat_id = toolChatId,
                            file_name = fileName,
                            size = fileBytes.Length
                        });
                    }
                }

                // Вариант 2: Отправка файла из пути
                if (args.TryGetValue("file_path", out var pathObj))
                {
                    string filePath = GetStringArg(pathObj);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        _logger.LogInformation("Отправка файла {FilePath} в чат {ChatId}", filePath, toolChatId);
                        await BotProvider.SendFileFromPathAsync(toolChatId, filePath, caption);

                        return JsonSerializer.Serialize(new
                        {
                            success = true,
                            message = "Файл отправлен",
                            chat_id = toolChatId,
                            file_name = fileName,
                            file_path = filePath
                        });
                    }
                }

                return JsonSerializer.Serialize(new { error = "Требуется либо непустой content, либо непустой file_path" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке файла");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }
    }
}
