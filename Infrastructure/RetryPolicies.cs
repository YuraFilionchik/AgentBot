using System;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using Microsoft.Extensions.Logging;

namespace AgentBot.Infrastructure
{
    /// <summary>
    /// Централизованные политики повторных попыток (Polly).
    /// </summary>
    public static class RetryPolicies
    {
        /// <summary>
        /// Базовая политика ретраев для HTTP-запросов и API.
        /// Делает 3 попытки с экспоненциальной задержкой.
        /// </summary>
        public static AsyncRetryPolicy CreateDefaultRetryPolicy(ILogger logger, string actionName)
        {
            return Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutException>()
                .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, retryCount, context) =>
                    {
                        logger.LogWarning(exception, "Ошибка при выполнении '{ActionName}'. Попытка {RetryCount} через {Delay} сек.", 
                            actionName, retryCount, timeSpan.TotalSeconds);
                    });
        }

        /// <summary>
        /// Специальная политика для Telegram API (может включать специфичные ошибки).
        /// </summary>
        public static AsyncRetryPolicy CreateTelegramRetryPolicy(ILogger logger)
        {
            return CreateDefaultRetryPolicy(logger, "Telegram API");
        }
    }
}
