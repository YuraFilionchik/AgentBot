using System;
using System.Threading;
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
        Task StartPollingAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Отправляет текстовое сообщение в указанный чат.
        /// </summary>
        Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Отправляет файл в указанный чат.
        /// </summary>
        Task SendFileAsync(long chatId, byte[] fileContent, string fileName, string? caption = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Отправляет файл из файловой системы.
        /// </summary>
        Task SendFileFromPathAsync(long chatId, string filePath, string? caption = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Колбек, вызываемый при получении входящего сообщения.
        /// Параметры: chatId, текст сообщения.
        /// </summary>
        Func<long, string, Task>? OnMessageReceived { get; set; }
    }
}
