using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AgentBot.Memory;
using AgentBot.Services;
using Polly;
using Polly.Retry;

namespace AgentBot.AiAgents
{
    public class OpenAiAgent : IAiAgent
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelName;
        private readonly string _apiKey;
        private readonly ILogger<OpenAiAgent> _logger;
        private readonly ILlmWrapper _llmWrapper;
        private readonly IConversationMemory _memory;
        private readonly AsyncRetryPolicy _retryPolicy;

        public OpenAiAgent(
            IConfiguration configuration,
            HttpClient httpClient,
            ILogger<OpenAiAgent> logger,
            ILlmWrapper llmWrapper,
            IConversationMemory memory)
        {
            _apiKey = configuration["AiAgent:ApiKey"] ?? throw new ArgumentException("OpenAI API key is missing");
            _modelName = configuration["AiAgent:Model"] ?? "gpt-4o";
            _httpClient = httpClient;
            _logger = logger;
            _llmWrapper = llmWrapper;
            _memory = memory;
            _httpClient.BaseAddress = new Uri(configuration["AiAgent:ApiUrl"] ?? "https://api.openai.com/v1/");

            // Инициализация политики ретраев
            _retryPolicy = AgentBot.Infrastructure.RetryPolicies.CreateDefaultRetryPolicy(logger, "OpenAI API");
        }

        public async Task<string> ProcessMessageAsync(long chatId, string message, List<IToolFunction> tools)
        {
            if (string.IsNullOrWhiteSpace(message)) return "Пустое сообщение!";

            _logger.LogInformation("Chat {ChatId}: OpenAI ← {Message}", chatId, message);

            // Формируем контекст пользователя
            var userContext = await _llmWrapper.BuildUserContextAsync(chatId, string.Empty, "user");
            userContext.AvailableTools.AddRange(tools.Select(t => new ToolInfo
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            }));

            // Формируем системный промпт
            string systemPrompt = _llmWrapper.BuildSystemPrompt(userContext);

            // Преобразуем сообщение с учётом алиасов
            string processedMessage = await _llmWrapper.BuildUserMessageAsync(chatId, message);

            // Получаем историю чата
            var savedHistory = await _memory.GetHistoryAsync(chatId, 20);
            var apiMessages = new List<object>();

            // Добавляем системный промпт
            apiMessages.Add(new { role = "system", content = systemPrompt });

            // Добавляем сохранённую историю
            foreach (var content in savedHistory)
            {
                apiMessages.Add(new
                {
                    role = content.Role,
                    content = string.Join("\n", content.Parts?.Select(p => p.Text) ?? Enumerable.Empty<string>())
                });
            }

            // Добавляем текущее сообщение
            var userMsg = new { role = "user", content = processedMessage };
            apiMessages.Add(userMsg);

            // Сохраняем в историю
            var userContent = new Google.GenAI.Types.Content { Role = "user" };
            userContent.Parts ??= new();
            userContent.Parts.Add(new Google.GenAI.Types.Part { Text = processedMessage });
            await _memory.AddMessageAsync(chatId, userContent);

            var openAiTools = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = new
                    {
                        type = "object",
                        properties = t.Parameters.ToDictionary(
                            p => p.Key,
                            p => new { type = p.Value.ToLower() == "number" ? "number" : "string" }
                        ),
                        required = t.Parameters.Keys.ToList()
                    }
                }
            }).ToList();

            int iterations = 0;

            while (iterations < 10)
            {
                var requestBody = new
                {
                    model = _modelName,
                    messages = apiMessages,
                    tools = openAiTools.Any() ? openAiTools : null,
                    tool_choice = openAiTools.Any() ? "auto" : null
                };

                var response = await _retryPolicy.ExecuteAsync(async () => 
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    request.Content = new StringContent(JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }), Encoding.UTF8, "application/json");
                    return await _httpClient.SendAsync(request);
                });

                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenAI API error: {Status} - {Response}", response.StatusCode, responseContent);
                    return "Произошла ошибка при обращении к ИИ.";
                }

                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    return "AI didn't provide a response.";

                var choice = choices[0];
                var messageNode = choice.GetProperty("message");

                string? answerText = messageNode.TryGetProperty("content", out var contentNode) && contentNode.ValueKind == JsonValueKind.String ? contentNode.GetString() : null;
                var toolCalls = messageNode.TryGetProperty("tool_calls", out var tcNode) && tcNode.ValueKind == JsonValueKind.Array ? tcNode : (JsonElement?)null;

                apiMessages.Add(JsonSerializer.Deserialize<object>(messageNode.GetRawText())!);

                if (toolCalls != null)
                {
                    foreach (var toolCall in toolCalls.Value.EnumerateArray())
                    {
                        var functionNode = toolCall.GetProperty("function");
                        string funcName = functionNode.GetProperty("name").GetString()!;
                        string argumentsJson = functionNode.GetProperty("arguments").GetString()!;

                        _logger.LogInformation("OpenAI Called function: {Name}", funcName);

                        var tool = tools.FirstOrDefault(t => t.Name == funcName);
                        string resultJson = "{}";

                        if (tool != null)
                        {
                            var args = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson) ?? new Dictionary<string, object>();
                            resultJson = await tool.ExecuteAsync(args, chatId);
                        }

                        apiMessages.Add(new
                        {
                            role = "tool",
                            tool_call_id = toolCall.GetProperty("id").GetString(),
                            name = funcName,
                            content = resultJson
                        });
                    }
                    iterations++;
                }
                else
                {
                    if (answerText != null)
                    {
                        var assistantContent = new Google.GenAI.Types.Content { Role = "assistant" };
                        assistantContent.Parts ??= new();
                        assistantContent.Parts.Add(new Google.GenAI.Types.Part { Text = answerText });
                        await _memory.AddMessageAsync(chatId, assistantContent);
                    }
                    return answerText ?? "No text response.";
                }
            }

            return "Слишком сложный запрос (превышен лимит вызовов инструментов).";
        }
    }
}
