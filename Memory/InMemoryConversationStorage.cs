using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Memory
{
    /// <summary>
    /// In-memory хранилище истории разговоров.
    /// Хранит историю в памяти с ограничением по количеству сообщений на чат.
    /// </summary>
    public class InMemoryConversationStorage : IConversationMemory
    {
        private readonly ConcurrentDictionary<long, List<Content>> _histories = new();
        private readonly ILogger<InMemoryConversationStorage> _logger;
        private readonly int _maxHistorySize;

        public InMemoryConversationStorage(
            ILogger<InMemoryConversationStorage> logger,
            IConfiguration config)
        {
            _logger = logger;
            _maxHistorySize = int.Parse(config["Memory:MaxMessagesPerChat"] ?? "20");
        }

        public Task AddMessageAsync(long chatId, Content message)
        {
            var history = _histories.GetOrAdd(chatId, _ => new List<Content>());
            history.Add(message);

            // Обрезаем старые сообщения, если превышен лимит
            if (history.Count > _maxHistorySize)
            {
                history.RemoveRange(0, history.Count - _maxHistorySize);
                _logger.LogDebug("Chat {ChatId}: история обрезана до {Count} сообщений", chatId, history.Count);
            }

            return Task.CompletedTask;
        }

        public Task<List<Content>> GetHistoryAsync(long chatId, int maxMessages)
        {
            if (_histories.TryGetValue(chatId, out var history))
            {
                var result = history.TakeLast(maxMessages).ToList();
                _logger.LogDebug("Chat {ChatId}: получено {Count} сообщений из истории", chatId, result.Count);
                return Task.FromResult(result);
            }

            _logger.LogDebug("Chat {ChatId}: история не найдена, возвращаем пустой список", chatId);
            return Task.FromResult(new List<Content>());
        }

        public Task ClearAsync(long chatId)
        {
            if (_histories.TryRemove(chatId, out _))
            {
                _logger.LogInformation("Chat {ChatId}: история очищена", chatId);
            }
            else
            {
                _logger.LogDebug("Chat {ChatId}: история уже пуста", chatId);
            }

            return Task.CompletedTask;
        }

        public async Task CleanupAsync()
        {
            // Базовая реализация: можно расширить для удаления неактивных чатов
            // Например, добавить timestamp последнего сообщения и удалять старые
            await Task.CompletedTask;
            _logger.LogDebug("Выполнена очистка неактивных чатов");
        }
    }
}
