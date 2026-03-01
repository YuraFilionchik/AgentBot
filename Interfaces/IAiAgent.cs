using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentBot
{
    /// <summary>
    /// Интерфейс для ИИ-агентов (OpenAI, Grok и др.).
    /// </summary>
    public interface IAiAgent
    {
        /// <summary>
        /// Обрабатывает входящее сообщение, взаимодействует с API ИИ
        /// (с поддержкой tool-функций) и возвращает текстовый ответ.
        /// </summary>
        Task<string> ProcessMessageAsync(string message, List<IToolFunction> tools);
    }
}
