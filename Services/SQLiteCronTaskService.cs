using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentBot.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Services
{
    /// <summary>
    /// SQLite-реализация сервиса управления Cron-задачами.
    /// </summary>
    public class SQLiteCronTaskService : ICronTaskService
    {
        private readonly string _connectionString;
        private readonly ILogger<SQLiteCronTaskService> _logger;

        public SQLiteCronTaskService(
            ILogger<SQLiteCronTaskService> logger,
            IConfiguration config)
        {
            _logger = logger;
            var dbPath = config["Cron:DatabasePath"] ?? "cron.db";
            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS CronTasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    CronExpression TEXT NOT NULL,
                    IsActive INTEGER DEFAULT 1,
                    LastRun TEXT,
                    NextRun TEXT,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_UserId ON CronTasks(UserId);
                CREATE INDEX IF NOT EXISTS IX_IsActive ON CronTasks(IsActive);
                CREATE INDEX IF NOT EXISTS IX_NextRun ON CronTasks(NextRun);
            ";
            command.ExecuteNonQuery();

            _logger.LogInformation("SQLite база данных Cron-задач инициализирована: {DbPath}", _connectionString);
        }

        public async Task<CronTask> CreateTaskAsync(long userId, string name, string description, string cronExpression)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var nextRun = GetNextOccurrence(cronExpression);

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO CronTasks (UserId, Name, Description, CronExpression, NextRun)
                VALUES ($userId, $name, $description, $cronExpr, $nextRun);
                SELECT * FROM CronTasks WHERE Id = last_insert_rowid();
            ";
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$description", description);
            command.Parameters.AddWithValue("$cronExpr", cronExpression);
            command.Parameters.AddWithValue("$nextRun", nextRun is not null ? nextRun.Value.ToString("o") : null);

            var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var task = ReadTask(reader);
                _logger.LogInformation("User {UserId}: создана задача '{Name}' с расписанием '{Cron}'", userId, name, cronExpression);
                return task;
            }

            throw new Exception("Не удалось создать задачу");
        }

        public async Task<bool> DeleteTaskAsync(long userId, long taskId)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM CronTasks WHERE Id = $id AND UserId = $userId;";
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$userId", userId);

            var rows = await command.ExecuteNonQueryAsync();
            if (rows > 0)
            {
                _logger.LogInformation("User {UserId}: удалена задача #{TaskId}", userId, taskId);
                return true;
            }

            return false;
        }

        public async Task<bool> ActivateTaskAsync(long userId, long taskId)
        {
            return await SetTaskActiveAsync(userId, taskId, true);
        }

        public async Task<bool> DeactivateTaskAsync(long userId, long taskId)
        {
            return await SetTaskActiveAsync(userId, taskId, false);
        }

        public async Task<List<CronTask>> GetAllTasksAsync(long userId)
        {
            return await LoadTasksAsync("SELECT * FROM CronTasks WHERE UserId = $userId ORDER BY Name;",
                new Dictionary<string, object> { ["$userId"] = userId });
        }

        public async Task<List<CronTask>> GetActiveTasksAsync(long userId)
        {
            return await LoadTasksAsync(
                "SELECT * FROM CronTasks WHERE UserId = $userId AND IsActive = 1 ORDER BY Name;",
                new Dictionary<string, object> { ["$userId"] = userId });
        }

        public async Task<CronTask?> GetTaskByIdAsync(long userId, long taskId)
        {
            var tasks = await LoadTasksAsync(
                "SELECT * FROM CronTasks WHERE Id = $id AND UserId = $userId;",
                new Dictionary<string, object> { ["$id"] = taskId, ["$userId"] = userId });
            return tasks.FirstOrDefault();
        }

        public async Task<List<CronTask>> GetDueTasksAsync()
        {
            var now = DateTime.UtcNow.ToString("o");
            return await LoadTasksAsync(
                "SELECT * FROM CronTasks WHERE IsActive = 1 AND NextRun IS NOT NULL AND NextRun <= $now;",
                new Dictionary<string, object> { ["$now"] = now });
        }

        public async Task MarkTaskAsRunAsync(long taskId)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Получаем текущую задачу
            var task = await GetTaskByIdAsync(0, taskId); // UserId = 0 для системного доступа
            if (task == null) return;

            var nextRun = GetNextOccurrence(task.CronExpression);

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE CronTasks
                SET LastRun = CURRENT_TIMESTAMP,
                    NextRun = $nextRun,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = $id;
            ";
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$nextRun", nextRun is not null ? nextRun.Value.ToString("o") : null);

            await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Задача #{TaskId} отмечена как выполненная, следующее выполнение: {NextRun}", taskId, nextRun);
        }

        public DateTime? GetNextOccurrence(string cronExpression)
        {
            // Простой парсер cron-выражений (5 полей)
            // Поддерживает: * , - /
            try
            {
                var parts = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5)
                    return null;

                var now = DateTime.UtcNow;
                var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);

                // Простая реализация: ищем следующее совпадение в пределах года
                for (int i = 0; i < 525600; i++) // 365 * 24 * 60 = 525600 минут в году
                {
                    if (MatchesCron(next, parts))
                        return next;
                    next = next.AddMinutes(1);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при вычислении следующего выполнения для cron: {Cron}", cronExpression);
                return null;
            }
        }

        private async Task<bool> SetTaskActiveAsync(long userId, long taskId, bool isActive)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE CronTasks
                SET IsActive = $active,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = $id AND UserId = $userId;
            ";
            command.Parameters.AddWithValue("$active", isActive ? 1 : 0);
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$userId", userId);

            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }

        private async Task<List<CronTask>> LoadTasksAsync(string sql, Dictionary<string, object> parameters)
        {
            var tasks = new List<CronTask>();
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tasks.Add(ReadTask(reader));
            }

            return tasks;
        }

        private static CronTask ReadTask(SqliteDataReader reader)
        {
            return new CronTask
            {
                Id = reader.GetInt64(0),
                UserId = reader.GetInt64(1),
                Name = reader.GetString(2),
                Description = reader.GetString(3),
                CronExpression = reader.GetString(4),
                IsActive = reader.GetInt32(5) == 1,
                LastRun = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                NextRun = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                CreatedAt = DateTime.Parse(reader.GetString(8)),
                UpdatedAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9))
            };
        }

        private static bool MatchesCron(DateTime time, string[] parts)
        {
            // parts: [минута, час, день, месяц, день недели]
            return MatchField(time.Minute, parts[0], 0, 59) &&
                   MatchField(time.Hour, parts[1], 0, 23) &&
                   MatchField(time.Day, parts[2], 1, 31) &&
                   MatchField((int)time.Month, parts[3], 1, 12) &&
                   MatchField((int)time.DayOfWeek, parts[4], 0, 6);
        }

        private static bool MatchField(int value, string pattern, int min, int max)
        {
            if (pattern == "*") return true;

            // Поддержка списков: 1,2,3
            if (pattern.Contains(","))
            {
                foreach (var part in pattern.Split(','))
                {
                    if (int.TryParse(part, out var v) && v == value)
                        return true;
                }
                return false;
            }

            // Поддержка диапазонов: 1-5
            if (pattern.Contains("-"))
            {
                var parts = pattern.Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var start) &&
                    int.TryParse(parts[1], out var end))
                {
                    return value >= start && value <= end;
                }
            }

            // Поддержка шагов: */5 или 0-30/5
            if (pattern.Contains("/"))
            {
                var slashParts = pattern.Split('/');
                if (slashParts.Length == 2 && int.TryParse(slashParts[1], out var step) && step > 0)
                {
                    if (slashParts[0] == "*")
                        return value % step == 0;

                    if (slashParts[0].Contains("-"))
                    {
                        var rangeParts = slashParts[0].Split('-');
                        if (rangeParts.Length == 2 &&
                            int.TryParse(rangeParts[0], out var rangeStart) &&
                            int.TryParse(rangeParts[1], out var rangeEnd))
                        {
                            return value >= rangeStart && value <= rangeEnd && (value - rangeStart) % step == 0;
                        }
                    }
                }
            }

            // Точное значение
            return int.TryParse(pattern, out var exact) && exact == value;
        }
    }
}
