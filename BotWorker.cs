using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentBot
{
    public class BotWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BotWorker> _logger;

        public BotWorker(IServiceProvider serviceProvider, ILogger<BotWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        private IBotProvider BotProvider => _serviceProvider.GetRequiredService<IBotProvider>();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BotWorker started.");
            await BotProvider.StartPollingAsync(stoppingToken);


            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("BotWorker stopping.");
        }
    }
}
