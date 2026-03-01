namespace AgentBot.Models
{
    /// <summary>
    /// Модель inline-кнопки для Telegram.
    /// </summary>
    public class InlineButton
    {
        /// <summary>
        /// Текст кнопки (отображается в сообщении).
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Данные кнопки (передаются в callback_query).
        /// Может быть командой, action'ом или JSON с параметрами.
        /// </summary>
        public string CallbackData { get; set; } = string.Empty;

        /// <summary>
        /// URL для открытия (опционально).
        /// Если указан, callback_data игнорируется.
        /// </summary>
        public string? Url { get; set; }
    }

    /// <summary>
    /// Модель строки inline-кнопок.
    /// </summary>
    public class InlineButtonRow
    {
        public List<InlineButton> Buttons { get; set; } = new();
    }

    /// <summary>
    /// Конфигурация inline-клавиатуры для отправки сообщения.
    /// </summary>
    public class InlineKeyboardConfig
    {
        /// <summary>
        /// Строки кнопок (каждая строка — отдельный ряд).
        /// </summary>
        public List<InlineButtonRow> Rows { get; set; } = new();

        /// <summary>
        /// Добавить стандартные кнопки управления.
        /// </summary>
        public bool IncludeStandardButtons { get; set; } = false;
    }
}
