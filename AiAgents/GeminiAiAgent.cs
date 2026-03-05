using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Google.GenAI;
using Google.GenAI.Types;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AgentBot.Memory;
using AgentBot.Services;
using Type = Google.GenAI.Types.Type;

namespace AgentBot.AiAgents
{
    /// <summary>
    /// Реализация IAiAgent для Google Gemini (официальный SDK Google.GenAI v1.2+)
    /// Использует IConversationMemory для хранения истории чатов.
    /// Использует ILlmWrapper для формирования контекста пользователя.
    /// </summary>
    public class GeminiAiAgent : IAiAgent
    {
        private readonly Client _client;
        private readonly string _modelName;
        private readonly ILogger<GeminiAiAgent> _logger;
        private readonly IConversationMemory _memory;
        private readonly ILlmWrapper _llmWrapper;
        private readonly int _maxToolIterations = 10;

        public GeminiAiAgent(
            IConfiguration configuration,
            ILogger<GeminiAiAgent> logger,
            IConversationMemory memory,
            ILlmWrapper llmWrapper)
        {
            var apiKey = configuration["AiAgent:ApiKey"]
                ?? throw new ArgumentException("Gemini API key is not configured in appsettings.json");

            _modelName = configuration["AiAgent:Model"] ?? "gemini-2.5-flash";
            _client = new Client(apiKey: apiKey);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _llmWrapper = llmWrapper ?? throw new ArgumentNullException(nameof(llmWrapper));
        }

        public async Task<string> ProcessMessageAsync(long chatId, string message, List<IToolFunction> tools)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Пустое сообщение. Напишите что-нибудь!";

            _logger.LogInformation("Chat {ChatId}: Gemini ← {Message}", chatId, message);

            // Формируем контекст пользователя
            var userContext = await _llmWrapper.BuildUserContextAsync(chatId, string.Empty, "user");
            userContext.AvailableTools.AddRange(tools.Select(t => new ToolInfo
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            }));

            // Формируем системный промпт
            string systemPrompt = _llmWrapper.BuildSystemPromptAsync(userContext);

            // Преобразуем сообщение с учётом алиасов
            string processedMessage = await _llmWrapper.BuildUserMessageAsync(chatId, message);

            // Получаем историю чата
            var history = await _memory.GetHistoryAsync(chatId, 20);

            // Добавляем системный промпт как первое сообщение (если история пуста)
            if (history.Count == 0)
            {
                var systemContent = new Content { Role = "user" };
                systemContent.Parts ??= new ();
                systemContent.Parts.Add(new Part { Text = systemPrompt });
                history.Add(systemContent);
            }

            // Добавляем сообщение пользователя в историю
            var userContent = new Content { Role = "user" };
            userContent.Parts ??= new ();
            userContent.Parts.Add(new Part { Text = processedMessage });
            history.Add(userContent);

            // Подготовка инструментов
            var functionDeclarations = tools.Select(ConvertToFunctionDeclaration).ToList();
            var tool = new Tool { FunctionDeclarations = functionDeclarations };

            int iteration = 0;

            while (iteration < _maxToolIterations)
            {
                try
                {
                    var config = new GenerateContentConfig();
                    config.Tools ??= new List<Tool>();
                    config.Tools.Add(tool);
                    config.ToolConfig = new ToolConfig
                    {
                        FunctionCallingConfig = new FunctionCallingConfig
                        {
                            Mode = FunctionCallingConfigMode.Auto
                        }
                    };

                    var response = await _client.Models.GenerateContentAsync(
                        model: _modelName,
                        contents: history,
                        config: config);

                    var candidate = response.Candidates?.FirstOrDefault();
                    if (candidate?.Content == null)
                        return "Не удалось получить ответ от Gemini.";

                    var content = candidate.Content;

                    // Логируем роль для отладки
                    _logger.LogDebug("Chat {ChatId}: получена роль от Gemini: {Role}", chatId, content.Role);

                    // Обработка function call
                    var functionCallParts = content.Parts?
                        .Where(p => p.FunctionCall != null)
                        .ToList() ?? new List<Part>();

                    if (functionCallParts.Any())
                    {
                        // Добавляем оригинальный content от модели целиком, чтобы сохранить thought signatures
                        history.Add(content);

                        var toolResponseParts = new List<Part>();

                        foreach (var part in functionCallParts)
                        {
                            var fc = part.FunctionCall!;
                            _logger.LogInformation("Chat {ChatId}: Gemini вызвал функцию: {Name}", chatId, fc.Name);

                            var toolFunc = tools.FirstOrDefault(t => t.Name.Equals(fc.Name, StringComparison.OrdinalIgnoreCase));
                            if (toolFunc == null)
                            {
                                _logger.LogWarning("Chat {ChatId}: Tool not found: {Name}", chatId, fc.Name);
                                continue;
                            }

                            var args = fc.Args ?? new Dictionary<string, object>();
                            _logger.LogDebug("Chat {ChatId}: Аргументы функции {Name}: {Args}", chatId, fc.Name, JsonSerializer.Serialize(args));

                            string resultJson = await toolFunc.ExecuteAsync(args, chatId);

                            var responsePart = new Part
                            {
                                FunctionResponse = new FunctionResponse
                                {
                                    Name = fc.Name,
                                    Response = JsonToDict(resultJson)
                                }
                            };

                            toolResponseParts.Add(responsePart);
                        }

                        if (toolResponseParts.Count == 0)
                        {
                            _logger.LogWarning("Chat {ChatId}: Не удалось сформировать ответы инструментов для {Count} вызовов", chatId, functionCallParts.Count);
                            return "Не удалось выполнить запрошенные инструменты.";
                        }

                        // Добавляем function response в историю (только в текущий список, не в персистентное хранилище)
                        var toolResponseContent = new Content
                        {
                            Role = "tool",
                            Parts = toolResponseParts
                        };
                        history.Add(toolResponseContent);

                        // Логируем историю для отладки
                        _logger.LogDebug("Chat {ChatId}: история для следующего запроса ({Count} сообщений):", chatId, history.Count);
                        foreach (var h in history)
                        {
                            var partTypes = string.Join(", ", h.Parts?.Select(p =>
                                p.Text != null ? "text" :
                                p.FunctionCall != null ? $"functionCall({p.FunctionCall.Name})" :
                                p.FunctionResponse != null ? $"functionResponse({p.FunctionResponse.Name})" :
                                "unknown") ?? Enumerable.Empty<string>());
                            _logger.LogDebug("  Role={Role}, Parts=[{PartTypes}]", h.Role, partTypes);
                        }

                        iteration++;
                        continue;
                    }

                    // Финальный текстовый ответ
                    var textPart = content.Parts?.FirstOrDefault(p => !string.IsNullOrEmpty(p.Text));
                    if (textPart != null)
                    {
                        string finalAnswer = textPart.Text!;
                        _logger.LogInformation("Chat {ChatId}: Gemini → {Answer}", chatId, finalAnswer);

                        // Сохраняем ответ в историю
                        await _memory.AddMessageAsync(chatId, content);
                        return finalAnswer;
                    }

                    return "Gemini не смог сгенерировать ответ.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chat {ChatId}: Ошибка при вызове Gemini", chatId);
                    return "Произошла ошибка при обращении к ИИ 😔 Попробуйте позже.";
                }
            }

            return "Слишком сложный запрос — превышено количество итераций инструментов.";
        }

        // ────────────────────────────────────────────────
        // Вспомогательные методы
        // ────────────────────────────────────────────────

        private static FunctionDeclaration ConvertToFunctionDeclaration(IToolFunction tool)
        {
            var schema = new Schema
            {
                Type = Type.Object,
                Properties = tool.Parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => GetSchema(kvp.Value)
                ),
                Required = tool.Parameters.Keys.ToList()
            };

            return new FunctionDeclaration
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = schema
            };
        }

        private static Schema GetSchema(string typeStr, string? description = null)
        {
            var lowerType = typeStr.ToLower();
            var schema = new Schema
            {
                Type = lowerType switch
                {
                    "string" => Google.GenAI.Types.Type.String,
                    "number" => Google.GenAI.Types.Type.Number,
                    "integer" => Google.GenAI.Types.Type.Integer,
                    "boolean" => Google.GenAI.Types.Type.Boolean,
                    "array" => Google.GenAI.Types.Type.Array,
                    _ => Google.GenAI.Types.Type.String
                }
            };

            // Для массивов указываем схему элементов (по умолчанию - строки)
            if (lowerType == "array")
            {
                schema.Items = new Schema { Type = Google.GenAI.Types.Type.Object };
            }

            if (!string.IsNullOrEmpty(description))
            {
                schema.Description = description;
            }

            return schema;
        }

        private static Struct JsonToStruct(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Struct();

            try
            {
                var doc = JsonDocument.Parse(json);
                var s = new Struct();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    s.Fields.Add(prop.Name, JsonElementToValue(prop.Value));
                }
                return s;
            }
            catch
            {
                return new Struct();
            }
        }

        private static Dictionary<string, object> JsonToDict(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();

            try
            {
                var doc = JsonDocument.Parse(json);
                var dict = new Dictionary<string, object>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = JsonElementToValue(prop.Value);
                }
                return dict;
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static Value JsonElementToValue(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => Value.ForString(e.GetString()),
            JsonValueKind.Number => Value.ForNumber(e.GetDouble()),
            JsonValueKind.True => Value.ForBool(true),
            JsonValueKind.False => Value.ForBool(false),
            JsonValueKind.Null => Value.ForNull(),
            _ => Value.ForString(e.ToString())
        };

        private static object ValueToObject(Value v) 
        {
            if (v == null) return string.Empty;
            return v.KindCase switch
            {
                Value.KindOneofCase.StringValue => v.StringValue ?? string.Empty,
                Value.KindOneofCase.NumberValue => v.NumberValue,
                Value.KindOneofCase.BoolValue => v.BoolValue,
                Value.KindOneofCase.NullValue => null!,
                _ => v.ToString() ?? string.Empty
            };
        }
    }
}
