using Telegram.Bot.Types.ReplyMarkups;

namespace AgentBot.Services
{
    /// <summary>
    /// Модель кнопки для быстрого доступа.
    /// </summary>
    public class QuickCommand
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public long UserId { get; set; }
    }
}
