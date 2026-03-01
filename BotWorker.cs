using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentBot
{
    public class BotWorker : BackgroundService
    {
        private readonly IBotProvider _botProvider;
        private readonly ILogger<BotWorker> _logger;

        public BotWorker(IBotProvider botProvider, ILogger<BotWorker> logger)
        {
            _botProvider = botProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BotWorker started.");
            await _botProvider.StartPollingAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                // Опционально: Дополнительная логика, если нужно
                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("BotWorker stopping.");
        }
    }
}
