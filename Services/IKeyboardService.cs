using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Types.ReplyMarkups;

namespace AgentBot.Services
{
    /// <summary>
    /// Сервис управления клавиатурами Telegram.
    /// </summary>
    public interface IKeyboardService
    {
        /// <summary>
        /// Получает основную ReplyKeyboardMarkup для пользователя.
        /// </summary>
        Task<ReplyKeyboardMarkup> GetMainKeyboardAsync(long userId);

        /// <summary>
        /// Получает InlineKeyboardMarkup для расширенных действий.
        /// </summary>
        Task<InlineKeyboardMarkup> GetInlineKeyboardAsync(long userId, string context);

        /// <summary>
        /// Добавляет быструю команду.
        /// </summary>
        Task AddQuickCommandAsync(long userId, string label, string command);

        /// <summary>
        /// Удаляет быструю команду.
        /// </summary>
        Task RemoveQuickCommandAsync(long userId, string commandId);

        /// <summary>
        /// Получает все быстрые команды пользователя.
        /// </summary>
        Task<List<QuickCommand>> GetQuickCommandsAsync(long userId);
    }
}
