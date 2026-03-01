namespace AgentBot.Models
{
    /// <summary>
    /// Модель алиаса/знания для пользователя.
    /// Алиасы позволяют сопоставлять пользовательские термины командам или описаниям.
    /// </summary>
    public class Alias
    {
        /// <summary>
        /// Уникальный идентификатор алиаса.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Идентификатор пользователя (Telegram Chat ID).
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// Алиас (пользовательский термин или сокращение).
        /// Например: "погода", "заметки", "cymmes".
        /// </summary>
        public string AliasName { get; set; } = string.Empty;

        /// <summary>
        /// Значение алиаса.
        /// Может быть командой (например, "/weather") или описанием (например, "приложение blazortool").
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Тип алиаса.
        /// </summary>
        public AliasType Type { get; set; }

        /// <summary>
        /// Дата создания алиаса.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Дата последнего изменения алиаса.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Типы алиасов.
    /// </summary>
    public enum AliasType
    {
        /// <summary>
        /// Алиас команды (например, "погода" → "/weather").
        /// </summary>
        Command = 0,

        /// <summary>
        /// Алиас знания (например, "cymmes" → "приложение blazortool").
        /// </summary>
        Knowledge = 1
    }
}
