using System.Collections.Generic;
using System.Threading.Tasks;
using AgentBot.Models;

namespace AgentBot.Services
{
    /// <summary>
    /// Сервис управления Cron-задачами.
    /// </summary>
    public interface ICronTaskService
    {
        /// <summary>
        /// Создаёт новую задачу.
        /// </summary>
        Task<CronTask> CreateTaskAsync(long userId, string name, string description, string cronExpression);

        /// <summary>
        /// Удаляет задачу по ID.
        /// </summary>
        Task<bool> DeleteTaskAsync(long userId, long taskId);

        /// <summary>
        /// Активирует задачу.
        /// </summary>
        Task<bool> ActivateTaskAsync(long userId, long taskId);

        /// <summary>
        /// Деактивирует задачу.
        /// </summary>
        Task<bool> DeactivateTaskAsync(long userId, long taskId);

        /// <summary>
        /// Получает все задачи пользователя.
        /// </summary>
        Task<List<CronTask>> GetAllTasksAsync(long userId);

        /// <summary>
        /// Получает активные задачи.
        /// </summary>
        Task<List<CronTask>> GetActiveTasksAsync(long userId);

        /// <summary>
        /// Получает задачу по ID.
        /// </summary>
        Task<CronTask?> GetTaskByIdAsync(long userId, long taskId);

        /// <summary>
        /// Получает задачи, готовые к выполнению.
        /// </summary>
        Task<List<CronTask>> GetDueTasksAsync();

        /// <summary>
        /// Отмечает задачу как выполненную.
        /// </summary>
        Task MarkTaskAsRunAsync(long taskId);

        /// <summary>
        /// Вычисляет следующее время выполнения задачи.
        /// </summary>
        DateTime? GetNextOccurrence(string cronExpression);
    }
}
