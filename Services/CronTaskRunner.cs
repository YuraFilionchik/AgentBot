using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentBot.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentBot.Services
{
    /// <summary>
    /// Фоновый сервис для выполнения Cron-задач.
    /// Проверяет и выполняет задачи по расписанию.
    /// </summary>
    public class CronTaskRunner : BackgroundService
    {
        private readonly ICronTaskService _cronTaskService;
        private readonly IAiAgent _aiAgent;
        private readonly IBotProvider _botProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CronTaskRunner> _logger;
        private readonly List<IToolFunction> _tools;

        public CronTaskRunner(
            ICronTaskService cronTaskService,
            IAiAgent aiAgent,
            IBotProvider botProvider,
            IServiceProvider serviceProvider,
            ILogger<CronTaskRunner> logger,
            IEnumerable<IToolFunction> tools)
        {
            _cronTaskService = cronTaskService;
            _aiAgent = aiAgent;
            _botProvider = botProvider;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _tools = tools?.ToList() ?? new List<IToolFunction>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CronTaskRunner запущен");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Проверяем задачи каждую минуту
                    var dueTasks = await _cronTaskService.GetDueTasksAsync();

                    foreach (var task in dueTasks)
                    {
                        _logger.LogInformation("Выполнение задачи #{TaskId}: {Name}", task.Id, task.Name);

                        try
                        {
                            // Выполняем задачу через ИИ-агент
                            string response = await _aiAgent.ProcessMessageAsync(
                                task.UserId,
                                task.Description,
                                _tools);

                            // Отправляем результат пользователю
                            await _botProvider.SendMessageAsync(task.UserId,
                                $"⏰ Результат задачи \"{task.Name}\":\n{response}");

                            // Отмечаем задачу как выполненную
                            await _cronTaskService.MarkTaskAsRunAsync(task.Id);

                            _logger.LogInformation("Задача #{TaskId} выполнена успешно", task.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка при выполнении задачи #{TaskId}", task.Id);
                            await _botProvider.SendMessageAsync(task.UserId,
                                $"⚠️ Ошибка при выполнении задачи \"{task.Name}\":\n{ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в CronTaskRunner");
                }

                // Ждём 1 минуту до следующей проверки
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("CronTaskRunner остановлен");
        }
    }
}
