using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgentBot.Services;
using Microsoft.Extensions.Logging;

namespace AgentBot.Tools
{
    /// <summary>
    /// Tool for AI agent to manage cron tasks: create, list, get, delete, activate, deactivate.
    /// Register in DI as AddTransient&lt;IToolFunction, CronTool&gt;().
    /// </summary>
    public class CronTool : IToolFunction
    {
        public string Name => "CronManager";

        public string Description =>
            "Manage scheduled cron tasks. " +
            "Actions: create (creates a new cron task), list (lists all tasks for the user), " +
            "get (get a single task by id), delete (removes a task by id), " +
            "activate (enables a task), deactivate (disables a task). " +
            "For 'create': provide name, cron_expression (5 fields: minute hour day month weekday, e.g. '0 8 * * *'), and description. " +
            "For 'get', 'delete', 'activate', 'deactivate': provide task_id. " +
            "For 'list': no extra parameters needed.";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string: create | list | get | delete | activate | deactivate" },
            { "name", "string: task name (for create)" },
            { "cron_expression", "string: 5-field cron expression like '0 8 * * *' (for create)" },
            { "description", "string: task description / prompt for the AI to execute (for create)" },
            { "task_id", "integer: task ID (for get, delete, activate, deactivate)" }
        };

        private readonly ICronTaskService _cronTaskService;
        private readonly ILogger<CronTool> _logger;

        public CronTool(ICronTaskService cronTaskService, ILogger<CronTool> logger)
        {
            _cronTaskService = cronTaskService ?? throw new ArgumentNullException(nameof(cronTaskService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> ExecuteAsync(Dictionary<string, object> args, long chatId = default)
        {
            string action = GetStringArg(args, "action").ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action))
                return JsonSerializer.Serialize(new { error = "Missing 'action' parameter. Supported: create, list, get, delete, activate, deactivate." });

            _logger.LogInformation("CronTool: action={Action}, chatId={ChatId}", action, chatId);

            return action switch
            {
                "create" => await HandleCreateAsync(args, chatId),
                "list" => await HandleListAsync(chatId),
                "get" => await HandleGetAsync(args, chatId),
                "delete" => await HandleDeleteAsync(args, chatId),
                "activate" => await HandleActivateAsync(args, chatId),
                "deactivate" => await HandleDeactivateAsync(args, chatId),
                _ => JsonSerializer.Serialize(new { error = $"Unknown action '{action}'. Supported: create, list, get, delete, activate, deactivate." })
            };
        }

        private async Task<string> HandleCreateAsync(Dictionary<string, object> args, long chatId)
        {
            string name = GetStringArg(args, "name");
            string cronExpression = GetStringArg(args, "cron_expression");
            string description = GetStringArg(args, "description");

            if (string.IsNullOrWhiteSpace(name))
                return JsonSerializer.Serialize(new { error = "Missing 'name' parameter." });

            if (string.IsNullOrWhiteSpace(cronExpression))
                return JsonSerializer.Serialize(new { error = "Missing 'cron_expression' parameter." });

            if (string.IsNullOrWhiteSpace(description))
                return JsonSerializer.Serialize(new { error = "Missing 'description' parameter." });

            var nextRun = _cronTaskService.GetNextOccurrence(cronExpression);
            if (nextRun is null)
                return JsonSerializer.Serialize(new { error = $"Invalid cron expression: '{cronExpression}'. Use 5 fields: minute hour day month weekday." });

            try
            {
                var task = await _cronTaskService.CreateTaskAsync(chatId, name, description, cronExpression);
                _logger.LogInformation("CronTool: created task #{TaskId} '{Name}' for chatId={ChatId}", task.Id, name, chatId);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    task = FormatTask(task),
                    next_run = nextRun.Value.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CronTool: error creating task '{Name}' for chatId={ChatId}", name, chatId);
                return JsonSerializer.Serialize(new { error = "Failed to create cron task: " + ex.Message });
            }
        }

        private async Task<string> HandleListAsync(long chatId)
        {
            var tasks = await _cronTaskService.GetAllTasksAsync(chatId);

            return JsonSerializer.Serialize(new
            {
                success = true,
                count = tasks.Count,
                tasks = tasks.Select(FormatTask).ToList()
            });
        }

        private async Task<string> HandleGetAsync(Dictionary<string, object> args, long chatId)
        {
            long? taskId = GetLongArg(args, "task_id");
            if (taskId is null)
                return JsonSerializer.Serialize(new { error = "Missing or invalid 'task_id' parameter." });

            var task = await _cronTaskService.GetTaskByIdAsync(chatId, taskId.Value);
            if (task is null)
                return JsonSerializer.Serialize(new { error = $"Task #{taskId} not found." });

            return JsonSerializer.Serialize(new { success = true, task = FormatTask(task) });
        }

        private async Task<string> HandleDeleteAsync(Dictionary<string, object> args, long chatId)
        {
            long? taskId = GetLongArg(args, "task_id");
            if (taskId is null)
                return JsonSerializer.Serialize(new { error = "Missing or invalid 'task_id' parameter." });

            bool deleted = await _cronTaskService.DeleteTaskAsync(chatId, taskId.Value);
            if (!deleted)
                return JsonSerializer.Serialize(new { error = $"Task #{taskId} not found or access denied." });

            _logger.LogInformation("CronTool: deleted task #{TaskId} for chatId={ChatId}", taskId, chatId);
            return JsonSerializer.Serialize(new { success = true, message = $"Task #{taskId} deleted." });
        }

        private async Task<string> HandleActivateAsync(Dictionary<string, object> args, long chatId)
        {
            long? taskId = GetLongArg(args, "task_id");
            if (taskId is null)
                return JsonSerializer.Serialize(new { error = "Missing or invalid 'task_id' parameter." });

            bool activated = await _cronTaskService.ActivateTaskAsync(chatId, taskId.Value);
            if (!activated)
                return JsonSerializer.Serialize(new { error = $"Task #{taskId} not found or access denied." });

            _logger.LogInformation("CronTool: activated task #{TaskId} for chatId={ChatId}", taskId, chatId);
            return JsonSerializer.Serialize(new { success = true, message = $"Task #{taskId} activated." });
        }

        private async Task<string> HandleDeactivateAsync(Dictionary<string, object> args, long chatId)
        {
            long? taskId = GetLongArg(args, "task_id");
            if (taskId is null)
                return JsonSerializer.Serialize(new { error = "Missing or invalid 'task_id' parameter." });

            bool deactivated = await _cronTaskService.DeactivateTaskAsync(chatId, taskId.Value);
            if (!deactivated)
                return JsonSerializer.Serialize(new { error = $"Task #{taskId} not found or access denied." });

            _logger.LogInformation("CronTool: deactivated task #{TaskId} for chatId={ChatId}", taskId, chatId);
            return JsonSerializer.Serialize(new { success = true, message = $"Task #{taskId} deactivated." });
        }

        private static object FormatTask(Models.CronTask task) => new
        {
            id = task.Id,
            name = task.Name,
            description = task.Description,
            cron_expression = task.CronExpression,
            is_active = task.IsActive,
            last_run = task.LastRun?.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
            next_run = task.NextRun?.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
            created_at = task.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
        };

        private static string GetStringArg(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var value))
                return string.Empty;

            return value switch
            {
                string s => s,
                JsonElement je => je.GetString() ?? string.Empty,
                null => string.Empty,
                _ => value.ToString() ?? string.Empty
            };
        }

        private static long? GetLongArg(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var value))
                return null;

            return value switch
            {
                long l => l,
                int i => i,
                double d => (long)d,
                JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt64(),
                JsonElement je when je.ValueKind == JsonValueKind.String =>
                    long.TryParse(je.GetString(), out var parsed) ? parsed : null,
                string s => long.TryParse(s, out var parsed) ? parsed : null,
                _ => null
            };
        }
    }
}
