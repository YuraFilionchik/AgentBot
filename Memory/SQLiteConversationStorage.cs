using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Google.GenAI.Types;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Memory
{
    /// <summary>
    /// SQLite-хранилище истории разговоров.
    /// Сохраняет историю в локальную SQLite-базу данных.
    /// </summary>
    public class SQLiteConversationStorage : IConversationMemory
    {
        private readonly string _connectionString;
        private readonly ILogger<SQLiteConversationStorage> _logger;
        private readonly int _maxHistorySize;
        private readonly JsonSerializerOptions _jsonOptions;

        public SQLiteConversationStorage(
            ILogger<SQLiteConversationStorage> logger,
            IConfiguration config)
        {
            _logger = logger;
            _maxHistorySize = int.Parse(config["Memory:MaxMessagesPerChat"] ?? "20");
            
            var dbPath = config["Memory:DatabasePath"] ?? "conversations.db";
            _connectionString = $"Data Source={dbPath}";
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS ConversationHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChatId INTEGER NOT NULL,
                    Role TEXT NOT NULL,
                    PartsJson TEXT NOT NULL,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS IX_ChatId ON ConversationHistory(ChatId);
            ";
            command.ExecuteNonQuery();

            _logger.LogInformation("SQLite база данных инициализирована: {DbPath}", _connectionString);
        }

        public async Task AddMessageAsync(long chatId, Content message)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var partsJson = JsonSerializer.Serialize(message.Parts, _jsonOptions);

            var addCommand = connection.CreateCommand();
            addCommand.CommandText = @"
                INSERT INTO ConversationHistory (ChatId, Role, PartsJson)
                VALUES ($chatId, $role, $partsJson);
            ";
            addCommand.Parameters.AddWithValue("$chatId", chatId);
            addCommand.Parameters.AddWithValue("$role", message.Role ?? "user");
            addCommand.Parameters.AddWithValue("$partsJson", partsJson);

            await addCommand.ExecuteNonQueryAsync();

            // Удаляем старые сообщения, если превышен лимит
            var cleanupCommand = connection.CreateCommand();
            cleanupCommand.CommandText = @"
                DELETE FROM ConversationHistory
                WHERE ChatId = $chatId AND Id NOT IN (
                    SELECT Id FROM ConversationHistory
                    WHERE ChatId = $chatId
                    ORDER BY Id DESC
                    LIMIT $limit
                );
            ";
            cleanupCommand.Parameters.AddWithValue("$chatId", chatId);
            cleanupCommand.Parameters.AddWithValue("$limit", _maxHistorySize);

            var deleted = await cleanupCommand.ExecuteNonQueryAsync();
            if (deleted > 0)
            {
                _logger.LogDebug("Chat {ChatId}: удалено {Deleted} старых сообщений", chatId, deleted);
            }
        }

        public async Task<List<Content>> GetHistoryAsync(long chatId, int maxMessages)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Role, PartsJson FROM ConversationHistory
                WHERE ChatId = $chatId
                ORDER BY Id DESC
                LIMIT $limit;
            ";
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$limit", maxMessages);

            var history = new List<Content>();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var role = reader.GetString(0);
                var partsJson = reader.GetString(1);
                var parts = JsonSerializer.Deserialize<List<Part>>(partsJson, _jsonOptions) ?? new List<Part>();

                var content = new Content { Role = role };
                content.Parts ??= new ();
                content.Parts.AddRange(parts);
                history.Add(content);
            }

            // Возвращаем в правильном порядке (от старых к новым)
            history.Reverse();
            _logger.LogDebug("Chat {ChatId}: получено {Count} сообщений из БД", chatId, history.Count);

            return history;
        }

        public async Task ClearAsync(long chatId)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ConversationHistory WHERE ChatId = $chatId;";
            command.Parameters.AddWithValue("$chatId", chatId);

            var deleted = await command.ExecuteNonQueryAsync();
            _logger.LogInformation("Chat {ChatId}: удалено {Deleted} сообщений из БД", chatId, deleted);
        }

        public async Task CleanupAsync()
        {
            // Удаляем чаты, в которых не было сообщений более 7 дней
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM ConversationHistory
                WHERE ChatId IN (
                    SELECT ChatId FROM ConversationHistory
                    GROUP BY ChatId
                    HAVING MAX(CreatedAt) < datetime('now', '-7 days')
                );
            ";

            var deleted = await command.ExecuteNonQueryAsync();
            if (deleted > 0)
            {
                _logger.LogInformation("Очистка: удалено {Deleted} сообщений из неактивных чатов", deleted);
            }
        }
    }
}
