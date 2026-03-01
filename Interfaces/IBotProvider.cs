using System;
using System.Threading.Tasks;

namespace AgentBot
{
    /// <summary>
    /// Абстрактный интерфейс для ботов (Telegram, Discord и др.).
    /// </summary>
    public interface IBotProvider
    {
        /// <summary>
        /// Запускает получение входящих сообщений (polling / webhook).
        /// </summary>
        Task StartPollingAsync();

        /// <summary>
        /// Отправляет текстовое сообщение в указанный чат.
        /// </summary>
        Task SendMessageAsync(long chatId, string text);

        /// <summary>
        /// Колбек, вызываемый при получении входящего сообщения.
        /// Параметры: chatId, текст сообщения.
        /// </summary>
        Func<long, string, Task>? OnMessageReceived { get; set; }
    }
}
