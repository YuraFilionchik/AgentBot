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
            "Отправить файл пользователю в Telegram. " +
            "Используйте, когда нужно отправить документ, отчёт, лог-файл или другие данные. " +
            "Файл может быть создан из содержимого (content) или прочитан из пути (file_path).";

        public Dictionary<string, string> Parameters => new()
        {
            { "chat_id", "number" },      // Telegram Chat ID
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

        private static bool TryGetLong(object? obj, out long value)
        {
            value = 0;
            if (obj == null) return false;
            
            if (obj is long l) { value = l; return true; }
            if (obj is int i) { value = i; return true; }
            if (obj is double d) { value = (long)d; return true; }
            
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out value))
                    return true;
                if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out value))
                    return true;
            }
            
            if (long.TryParse(obj.ToString(), out value))
                return true;
                
            return false;
        }

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
                if (!args.TryGetValue("chat_id", out var chatIdObj) || !TryGetLong(chatIdObj, out long chatId))
                {
                    return JsonSerializer.Serialize(new { error = "chat_id обязателен и должен быть числом" });
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
                        _logger.LogInformation("Отправка файла {FileName} в чат {ChatId} (из содержимого)", fileName, chatId);
                        await BotProvider.SendFileAsync(chatId, fileBytes, fileName, caption);

                        return JsonSerializer.Serialize(new
                        {
                            success = true,
                            message = "Файл отправлен",
                            chat_id = chatId,
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
                        _logger.LogInformation("Отправка файла {FilePath} в чат {ChatId}", filePath, chatId);
                        await BotProvider.SendFileFromPathAsync(chatId, filePath, caption);

                        return JsonSerializer.Serialize(new
                        {
                            success = true,
                            message = "Файл отправлен",
                            chat_id = chatId,
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
