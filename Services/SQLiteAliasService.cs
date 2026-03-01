using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgentBot.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Services
{
    /// <summary>
    /// SQLite-реализация сервиса управления алиасами.
    /// </summary>
    public class SQLiteAliasService : IAliasService
    {
        private readonly string _connectionString;
        private readonly ILogger<SQLiteAliasService> _logger;

        public SQLiteAliasService(
            ILogger<SQLiteAliasService> logger,
            IConfiguration config)
        {
            _logger = logger;
            var dbPath = config["Alias:DatabasePath"] ?? "aliases.db";
            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Aliases (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    AliasName TEXT NOT NULL,
                    Value TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT,
                    UNIQUE(UserId, AliasName)
                );
                CREATE INDEX IF NOT EXISTS IX_UserId ON Aliases(UserId);
                CREATE INDEX IF NOT EXISTS IX_AliasName ON Aliases(AliasName);
            ";
            command.ExecuteNonQuery();

            _logger.LogInformation("SQLite база данных алиасов инициализирована: {DbPath}", _connectionString);
        }

        public async Task<Alias> AddAliasAsync(long userId, string aliasName, string value, AliasType type)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO Aliases (UserId, AliasName, Value, Type, UpdatedAt)
                VALUES ($userId, $aliasName, $value, $type, CURRENT_TIMESTAMP);
                SELECT Id, UserId, AliasName, Value, Type, CreatedAt, UpdatedAt 
                FROM Aliases WHERE UserId = $userId AND AliasName = $aliasName;
            ";
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$aliasName", aliasName.ToLowerInvariant());
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$type", (int)type);

            var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var alias = new Alias
                {
                    Id = reader.GetInt64(0),
                    UserId = reader.GetInt64(1),
                    AliasName = reader.GetString(2),
                    Value = reader.GetString(3),
                    Type = (AliasType)reader.GetInt32(4),
                    CreatedAt = DateTime.Parse(reader.GetString(5)),
                    UpdatedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6))
                };

                _logger.LogInformation("User {UserId}: добавлен алиас '{Alias}' → '{Value}'", userId, aliasName, value);
                return alias;
            }

            throw new Exception("Не удалось добавить алиас");
        }

        public async Task<bool> DeleteAliasAsync(long userId, string aliasName)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Aliases WHERE UserId = $userId AND AliasName = $aliasName;";
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$aliasName", aliasName.ToLowerInvariant());

            var rows = await command.ExecuteNonQueryAsync();
            if (rows > 0)
            {
                _logger.LogInformation("User {UserId}: удалён алиас '{Alias}'", userId, aliasName);
                return true;
            }

            return false;
        }

        public async Task<List<Alias>> GetAllAliasesAsync(long userId)
        {
            return await LoadAliasesAsync("SELECT * FROM Aliases WHERE UserId = $userId ORDER BY AliasName;",
                new Dictionary<string, object> { ["$userId"] = userId });
        }

        public async Task<Alias?> GetAliasByNameAsync(long userId, string aliasName)
        {
            var aliases = await LoadAliasesAsync(
                "SELECT * FROM Aliases WHERE UserId = $userId AND AliasName = $aliasName;",
                new Dictionary<string, object>
                {
                    ["$userId"] = userId,
                    ["$aliasName"] = aliasName.ToLowerInvariant()
                });
            return aliases.FirstOrDefault();
        }

        public async Task<List<Alias>> GetCommandAliasesAsync(long userId)
        {
            return await LoadAliasesAsync(
                "SELECT * FROM Aliases WHERE UserId = $userId AND Type = 0 ORDER BY AliasName;",
                new Dictionary<string, object> { ["$userId"] = userId });
        }

        public async Task<List<Alias>> GetKnowledgeAliasesAsync(long userId)
        {
            return await LoadAliasesAsync(
                "SELECT * FROM Aliases WHERE UserId = $userId AND Type = 1 ORDER BY AliasName;",
                new Dictionary<string, object> { ["$userId"] = userId });
        }

        public async Task<string> GetAliasesContextAsync(long userId)
        {
            var commandAliases = await GetCommandAliasesAsync(userId);
            var knowledgeAliases = await GetKnowledgeAliasesAsync(userId);

            var context = new System.Text.StringBuilder();
            context.AppendLine("### Пользовательские алиасы:");

            if (commandAliases.Any())
            {
                context.AppendLine("**Команды:**");
                foreach (var alias in commandAliases)
                {
                    context.AppendLine($"  - \"{alias.AliasName}\" → команда {alias.Value}");
                }
            }

            if (knowledgeAliases.Any())
            {
                context.AppendLine("**Знания:**");
                foreach (var alias in knowledgeAliases)
                {
                    context.AppendLine($"  - \"{alias.AliasName}\" — {alias.Value}");
                }
            }

            return context.ToString();
        }

        public async Task<string?> ResolveCommandAliasAsync(string text)
        {
            // Простая реализация: ищем точное совпадение первого слова с алиасом
            // В production нужно более умное разрешение
            var firstWord = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(firstWord))
                return null;

            // Ищем во всех базах (для простоты берём userId = 0 как глобальный)
            var aliases = await LoadAliasesAsync(
                "SELECT * FROM Aliases WHERE AliasName = $aliasName AND Type = 0;",
                new Dictionary<string, object> { ["$aliasName"] = firstWord });

            return aliases.FirstOrDefault()?.Value;
        }

        private async Task<List<Alias>> LoadAliasesAsync(string sql, Dictionary<string, object> parameters)
        {
            var aliases = new List<Alias>();
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
                aliases.Add(new Alias
                {
                    Id = reader.GetInt64(0),
                    UserId = reader.GetInt64(1),
                    AliasName = reader.GetString(2),
                    Value = reader.GetString(3),
                    Type = (AliasType)reader.GetInt32(4),
                    CreatedAt = DateTime.Parse(reader.GetString(5)),
                    UpdatedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6))
                });
            }

            return aliases;
        }
    }
}
