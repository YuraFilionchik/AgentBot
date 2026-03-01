using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentBot
{
    /// <summary>
    /// Контекст пользователя для формирования запроса к LLM.
    /// </summary>
    public class UserContext
    {
        /// <summary>
        /// Идентификатор пользователя (Telegram Chat ID).
        /// </summary>
        public long ChatId { get; set; }

        /// <summary>
        /// Имя пользователя.
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Роль пользователя (например, "user" или "admin").
        /// </summary>
        public string Role { get; set; } = "user";

        /// <summary>
        /// Список алиасов команд.
        /// </summary>
        public List<UserAlias> CommandAliases { get; set; } = new();

        /// <summary>
        /// Список алиасов знаний.
        /// </summary>
        public List<UserAlias> KnowledgeAliases { get; set; } = new();

        /// <summary>
        /// История переписки (последние сообщения).
        /// </summary>
        public List<ChatMessage> History { get; set; } = new();

        /// <summary>
        /// Список доступных инструментов.
        /// </summary>
        public List<ToolInfo> AvailableTools { get; set; } = new();
    }

    /// <summary>
    /// Алиас пользователя.
    /// </summary>
    public class UserAlias
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Сообщение в истории чата.
    /// </summary>
    public class ChatMessage
    {
        public string Role { get; set; } = "user"; // "user" или "assistant"
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Информация об инструменте для LLM.
    /// </summary>
    public class ToolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Результат обработки запроса к LLM.
    /// </summary>
    public class LlmResponse
    {
        /// <summary>
        /// Текстовый ответ.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Файл для отправки пользователю (если есть).
        /// </summary>
        public byte[]? FileContent { get; set; }

        /// <summary>
        /// Имя файла для отправки.
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// Были ли вызваны инструменты.
        /// </summary>
        public bool ToolsUsed { get; set; }

        /// <summary>
        /// Список вызванных инструментов.
        /// </summary>
        public List<string> UsedTools { get; set; } = new();
    }
}
