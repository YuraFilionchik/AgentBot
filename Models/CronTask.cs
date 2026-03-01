namespace AgentBot.Models
{
    /// <summary>
    /// Модель Cron-задачи для планирования действий по расписанию.
    /// </summary>
    public class CronTask
    {
        /// <summary>
        /// Уникальный идентификатор задачи.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Идентификатор пользователя (Telegram Chat ID).
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// Название задачи.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Описание задачи (текстовое задание для LLM).
        /// Например: "Проверить статус приложения cymmes и если оно не запущено, то запустить его".
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Cron-выражение (5 полей: минута, час, день, месяц, день недели).
        /// Например: "0 10 * * *" — каждый день в 10:00.
        /// </summary>
        public string CronExpression { get; set; } = string.Empty;

        /// <summary>
        /// Активна ли задача.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Дата последнего выполнения.
        /// </summary>
        public DateTime? LastRun { get; set; }

        /// <summary>
        /// Дата следующего выполнения.
        /// </summary>
        public DateTime? NextRun { get; set; }

        /// <summary>
        /// Дата создания задачи.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Дата последнего изменения.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
