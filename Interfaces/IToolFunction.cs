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
        Task<string> ExecuteAsync(Dictionary<string, object> args);
    }
}
