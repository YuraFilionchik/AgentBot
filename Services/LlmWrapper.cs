using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentBot;
using Microsoft.Extensions.Logging;

namespace AgentBot.Services
{
    /// <summary>
    /// Обёртка для формирования контекстного запроса к LLM.
    /// Включает информацию о пользователе, алиасах, истории и доступных инструментах.
    /// </summary>
    public interface ILlmWrapper
    {
        /// <summary>
        /// Формирует системный промпт с учётом контекста пользователя.
        /// </summary>
        string BuildSystemPromptAsync(UserContext context);

        /// <summary>
        /// Формирует пользовательское сообщение с учётом алиасов.
        /// </summary>
        Task<string> BuildUserMessageAsync(long chatId, string message);

        /// <summary>
        /// Создаёт полный контекст для запроса к LLM.
        /// </summary>
        Task<UserContext> BuildUserContextAsync(long chatId, string username, string role);
    }

    /// <summary>
    /// Реализация обёртки для LLM-запросов.
    /// </summary>
    public class LlmWrapper : ILlmWrapper
    {
        private readonly IAliasService _aliasService;
        private readonly ILogger<LlmWrapper> _logger;

        public LlmWrapper(IAliasService aliasService, ILogger<LlmWrapper> logger)
        {
            _aliasService = aliasService;
            _logger = logger;
        }

        public string BuildSystemPromptAsync(UserContext context)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Ты — умный помощник в Telegram-боте. Ты помогаешь пользователю, отвечая на его вопросы и выполняя команды. " +
                          "Ты можешь использовать доступные инструменты для получения информации или выполнения действий от имени пользователя.");
            sb.AppendLine("Отвечай на вопросы пользователя полезно и кратко.");
            sb.AppendLine();

            // Информация о пользователе
            sb.AppendLine($"### Информация о пользователе:");
            sb.AppendLine($"- Chat ID: {context.ChatId}");
            sb.AppendLine($"- Username: {context.Username ?? "не указан"}");
            sb.AppendLine($"- Роль: {context.Role}");
            sb.AppendLine();

            // Алиасы команд
            if (context.CommandAliases.Count > 0)
            {
                sb.AppendLine("### Пользовательские команды (алиасы):");
                sb.AppendLine("Если пользователь использует эти слова, он имеет в виду соответствующие команды:");
                foreach (var alias in context.CommandAliases)
                {
                    sb.AppendLine($"  - \"{alias.Name}\" → команда {alias.Value}");
                }
                sb.AppendLine();
            }

            // Алиасы знаний
            if (context.KnowledgeAliases.Count > 0)
            {
                sb.AppendLine("### База знаний пользователя:");
                sb.AppendLine("Запомни эти соответствия для использования в ответах:");
                foreach (var alias in context.KnowledgeAliases)
                {
                    sb.AppendLine($"  - \"{alias.Name}\" — {alias.Value}");
                }
                sb.AppendLine();
            }

            // Доступные инструменты
            if (context.AvailableTools.Count > 0)
            {
                sb.AppendLine("### Доступные инструменты:");
                foreach (var tool in context.AvailableTools)
                {
                    sb.AppendLine($"- {tool.Name}: {tool.Description}");
                }
                sb.AppendLine();
            }

            // Инструкция по использованию инструментов
            sb.AppendLine("### Инструкция:");
            sb.AppendLine("- Если для ответа нужен инструмент — вызови его.");
            sb.AppendLine("- Если пользователь просит сохранить данные в файл — используй SendFile для отправки файла.");
            sb.AppendLine("- Если ответ слишком длинный — используй SendFile для отправки файла с содержимым.");
            sb.AppendLine("- Для отправки текстового сообщения пользователю используй SendMessage.");
            sb.AppendLine("- Для отправки документа/файла используй SendFile (с content или file_path).");
            sb.AppendLine();
            sb.AppendLine("### Inline-кнопки в SendMessage:");
            sb.AppendLine("Для интерактивного взаимодействия добавляй inline-кнопки к сообщениям.");
            sb.AppendLine("Формат inline_buttons — массив строк с кнопками:");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"chat_id\": 123456789,");
            sb.AppendLine("  \"text\": \"Выберите действие:\",");
            sb.AppendLine("  \"inline_buttons\": [");
            sb.AppendLine("    [{\"label\": \"✅ Да\", \"callback_data\": \"confirm_yes\"}, {\"label\": \"❌ Нет\", \"callback_data\": \"confirm_no\"}],");
            sb.AppendLine("    [{\"label\": \"📊 Показать статистику\", \"callback_data\": \"show_stats\"}]");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("callback_data передаётся боту при нажатии кнопки (до 64 байт).");
            sb.AppendLine("Используй кнопки для: подтверждений, выбора опций, навигации, быстрых действий.");

            return sb.ToString();
        }

        public async Task<string> BuildUserMessageAsync(long chatId, string message)
        {
            // Проверяем, не является ли первое слово алиасом команды
            var firstWord = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstWord))
            {
                var resolvedCommand = await _aliasService.ResolveCommandAliasAsync(message);
                if (!string.IsNullOrEmpty(resolvedCommand))
                {
                    _logger.LogDebug("Алиас \"{Alias}\" разрешён в команду {Command}", firstWord, resolvedCommand);
                    // Заменяем алиас на команду в начале сообщения
                    return message.Replace(firstWord, resolvedCommand, StringComparison.OrdinalIgnoreCase);
                }
            }

            return message;
        }

        public async Task<UserContext> BuildUserContextAsync(long chatId, string username, string role)
        {
            var context = new UserContext
            {
                ChatId = chatId,
                Username = username,
                Role = role
            };

            // Загружаем алиасы
            var commandAliases = await _aliasService.GetCommandAliasesAsync(chatId);
            var knowledgeAliases = await _aliasService.GetKnowledgeAliasesAsync(chatId);

            context.CommandAliases.AddRange(commandAliases.Select(a => new UserAlias
            {
                Name = a.AliasName,
                Value = a.Value
            }));

            context.KnowledgeAliases.AddRange(knowledgeAliases.Select(a => new UserAlias
            {
                Name = a.AliasName,
                Value = a.Value
            }));

            _logger.LogDebug("Контекст для chatId={ChatId}: {CommandCount} командных алиасов, {KnowledgeCount} знаний",
                chatId, context.CommandAliases.Count, context.KnowledgeAliases.Count);

            return context;
        }
    }
}
