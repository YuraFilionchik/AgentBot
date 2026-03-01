using System.Collections.Generic;
using System.Threading.Tasks;
using Google.GenAI.Types;

namespace AgentBot.Memory
{
    /// <summary>
    /// Интерфейс для хранения истории разговоров (контекста ИИ).
    /// </summary>
    public interface IConversationMemory
    {
        /// <summary>
        /// Добавляет сообщение в историю чата.
        /// </summary>
        Task AddMessageAsync(long chatId, Content message);

        /// <summary>
        /// Получает историю чата.
        /// </summary>
        Task<List<Content>> GetHistoryAsync(long chatId, int maxMessages);

        /// <summary>
        /// Очищает историю чата.
        /// </summary>
        Task ClearAsync(long chatId);

        /// <summary>
        /// Выполняет очистку старых/неактивных чатов (для периодического вызова).
        /// </summary>
        Task CleanupAsync();
    }
}
