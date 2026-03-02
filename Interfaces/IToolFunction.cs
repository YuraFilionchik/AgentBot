using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentBot
{
    /// <summary>
    /// Интерфейс для функций (tools), доступных ИИ-агенту.
    /// </summary>
    public interface IToolFunction
    {
        /// <summary>
        /// Уникальное имя функции (например, "get_weather").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Описание функции для ИИ-агента.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Схема параметров: ключ — имя параметра, значение — описание/тип.
        /// </summary>
        Dictionary<string, string> Parameters { get; }

        /// <summary>
        /// Выполняет функцию с указанными аргументами и возвращает результат (строка или JSON).
        /// </summary>
        /// <param name="args">Аргументы функции</param>
        /// <param name="chatId">ID чата для проверки прав доступа (опционально)</param>
        Task<string> ExecuteAsync(Dictionary<string, object> args, long chatId = default);
    }
}
