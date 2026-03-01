using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AgentBot.Tools
{
    /// <summary>
    /// Инструмент для отправки текстового сообщения пользователю с inline-кнопками.
    /// </summary>
    public class SendMessageTool : IToolFunction
    {
        public string Name => "SendMessage";

        public string Description =>
            "Отправить текстовое сообщение пользователю в Telegram. " +
            "Поддерживает inline-кнопки для интерактивного взаимодействия. " +
            "Используйте, когда нужно отправить сообщение от имени бота с кнопками действий.";

        public Dictionary<string, string> Parameters => new()
        {
            { "chat_id", "number" },      // Telegram Chat ID
            { "text", "string" },         // Текст сообщения
            { "parse_mode", "string" },   // Опционально: "Markdown" или "HTML"
            { "inline_buttons", "array" } // Опционально: массив inline-кнопок
        };

        private readonly TelegramBotClient _client;
        private readonly ILogger<SendMessageTool> _logger;

        public SendMessageTool(
            IConfiguration configuration,
            ILogger<SendMessageTool> logger)
        {
            var token = configuration["Bots:Telegram:ApiToken"];
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Telegram API token is not configured.");
            }

            _client = new TelegramBotClient(token);
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(Dictionary<string, object> args)
        {
            try
            {
                if (!args.TryGetValue("chat_id", out var chatIdObj) || chatIdObj is not long chatId)
                {
                    return JsonSerializer.Serialize(new { error = "chat_id обязателен и должен быть числом" });
                }

                if (!args.TryGetValue("text", out var textObj) || textObj is not string text)
                {
                    return JsonSerializer.Serialize(new { error = "text обязателен и должен быть строкой" });
                }

                string? parseModeString = args.TryGetValue("parse_mode", out var modeObj) && modeObj is string m ? m : null;
                
                // Преобразуем строку в enum ParseMode
                ParseMode? parseMode = parseModeString switch
                {
                    "Markdown" or "markdown" => ParseMode.Markdown,
                    "MarkdownV2" or "markdownv2" => ParseMode.MarkdownV2,
                    "HTML" or "html" or "Html" => ParseMode.Html,
                    _ => null
                };

                // Парсим inline-кнопки
                InlineKeyboardMarkup? inlineKeyboard = null;
                if (args.TryGetValue("inline_buttons", out var buttonsObj) && buttonsObj != null)
                {
                    inlineKeyboard = BuildInlineKeyboard(buttonsObj);
                }

                _logger.LogInformation("Отправка сообщения в чат {ChatId}", chatId);

                // Отправляем сообщение с клавиатурой или без.
                // Если parseMode не указан — вызываем перегрузку без параметра.
                if (inlineKeyboard != null)
                {
                    if (parseMode.HasValue)
                    {
                        await _client.SendMessage(
                            chatId: new ChatId(chatId),
                            text: text,
                            parseMode: parseMode.Value,
                            replyMarkup: inlineKeyboard);
                    }
                    else
                    {
                        await _client.SendMessage(
                            chatId: new ChatId(chatId),
                            text: text,
                            replyMarkup: inlineKeyboard);
                    }
                }
                else
                {
                    if (parseMode.HasValue)
                    {
                        await _client.SendMessage(
                            chatId: new ChatId(chatId),
                            text: text,
                            parseMode: parseMode.Value);
                    }
                    else
                    {
                        await _client.SendMessage(
                            chatId: new ChatId(chatId),
                            text: text);
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Сообщение отправлено",
                    chat_id = chatId,
                    buttons_count = inlineKeyboard?.InlineKeyboard?.SelectMany(r => r).Count() ?? 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private InlineKeyboardMarkup BuildInlineKeyboard(object buttonsObj)
        {
            var rows = new List<List<InlineKeyboardButton>>();

            // Обрабатываем разные форматы входных данных
            if (buttonsObj is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    // Формат 1: Массив строк, где каждая строка — массив кнопок
                    // [[{"label": "Да", "callback_data": "yes"}], [{"label": "Нет", "callback_data": "no"}]]
                    foreach (var rowElement in element.EnumerateArray())
                    {
                        if (rowElement.ValueKind == JsonValueKind.Array)
                        {
                            var row = new List<InlineKeyboardButton>();
                            foreach (var btnElement in rowElement.EnumerateArray())
                            {
                                var button = ParseButton(btnElement);
                                if (button != null)
                                    row.Add(button);
                            }
                            if (row.Any())
                                rows.Add(row);
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.Object)
                {
                    // Формат 2: Объект с rows
                    // {"rows": [[{"label": "Да", "callback_data": "yes"}]]}
                    if (element.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var rowElement in rowsElement.EnumerateArray())
                        {
                            if (rowElement.ValueKind == JsonValueKind.Array)
                            {
                                var row = new List<InlineKeyboardButton>();
                                foreach (var btnElement in rowElement.EnumerateArray())
                                {
                                    var button = ParseButton(btnElement);
                                    if (button != null)
                                        row.Add(button);
                                }
                                if (row.Any())
                                    rows.Add(row);
                            }
                        }
                    }
                    // Формат 3: Простой массив кнопок (одна строка)
                    // [{"label": "Да", "callback_data": "yes"}, {"label": "Нет", "callback_data": "no"}]
                    else if (element.TryGetProperty("buttons", out var buttonsElement) && buttonsElement.ValueKind == JsonValueKind.Array)
                    {
                        var row = new List<InlineKeyboardButton>();
                        foreach (var btnElement in buttonsElement.EnumerateArray())
                        {
                            var button = ParseButton(btnElement);
                            if (button != null)
                                row.Add(button);
                        }
                        if (row.Any())
                            rows.Add(row);
                    }
                }
            }

            // Если ничего не распарсилось — создаём пустую клавиатуру
            if (!rows.Any())
            {
                rows.Add(new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData("ℹ️ Информация", "info")
                });
            }

            return new InlineKeyboardMarkup(rows);
        }

        private InlineKeyboardButton? ParseButton(JsonElement btnElement)
        {
            if (btnElement.ValueKind != JsonValueKind.Object)
                return null;

            string? label = null;
            string? callbackData = null;
            string? url = null;

            if (btnElement.TryGetProperty("label", out var labelElem))
                label = labelElem.GetString();

            if (btnElement.TryGetProperty("callback_data", out var cbElem))
                callbackData = cbElem.GetString();

            if (btnElement.TryGetProperty("url", out var urlElem))
                url = urlElem.GetString();

            // Также поддерживаем альтернативные имена полей
            if (string.IsNullOrEmpty(label) && btnElement.TryGetProperty("text", out var textElem))
                label = textElem.GetString();

            if (string.IsNullOrEmpty(callbackData) && btnElement.TryGetProperty("data", out var dataElem))
                callbackData = dataElem.GetString();

            if (string.IsNullOrEmpty(label))
                return null;

            // Если есть URL — создаём кнопку-ссылку
            if (!string.IsNullOrEmpty(url))
            {
                return InlineKeyboardButton.WithUrl(label, url);
            }

            // Иначе создаём callback-кнопку
            return InlineKeyboardButton.WithCallbackData(label, callbackData ?? label);
        }
    }
}
