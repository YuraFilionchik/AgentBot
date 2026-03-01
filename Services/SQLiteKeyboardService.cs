using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.ReplyMarkups;

namespace AgentBot.Services
{
    /// <summary>
    /// SQLite-реализация сервиса клавиатур.
    /// </summary>
    public class SQLiteKeyboardService : IKeyboardService
    {
        private readonly string _connectionString;
        private readonly ILogger<SQLiteKeyboardService> _logger;

        // Стандартные команды
        private static readonly List<(string Label, string Command)> DefaultCommands = new()
        {
            ("📋 Помощь", "/help"),
            ("📝 Заметки", "/note"),
            ("🌤 Погода", "/weather"),
            ("📚 Алиасы", "/listaliases"),
            ("⏰ Задачи", "/listcrons"),
            ("ℹ️ О боте", "/about")
        };

        public SQLiteKeyboardService(
            ILogger<SQLiteKeyboardService> logger,
            IConfiguration config)
        {
            _logger = logger;
            var dbPath = config["Keyboard:DatabasePath"] ?? "keyboard.db";
            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS QuickCommands (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Label TEXT NOT NULL,
                    Command TEXT NOT NULL,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(UserId, Command)
                );
                CREATE INDEX IF NOT EXISTS IX_UserId ON QuickCommands(UserId);
            ";
            command.ExecuteNonQuery();

            _logger.LogInformation("SQLite база данных клавиатур инициализирована: {DbPath}", _connectionString);
        }

        public Task<ReplyKeyboardMarkup> GetMainKeyboardAsync(long userId)
        {
            // Создаём клавиатуру с быстрыми командами
            var keyboard = new List<List<KeyboardButton>>();

            // Первый ряд: стандартные команды
            var row1 = new List<KeyboardButton>();
            foreach (var (label, cmd) in DefaultCommands.Take(3))
            {
                row1.Add(new KeyboardButton(label) { RequestUsers = null });
            }
            keyboard.Add(row1);

            var row2 = new List<KeyboardButton>();
            foreach (var (label, cmd) in DefaultCommands.Skip(3).Take(3))
            {
                row2.Add(new KeyboardButton(label) { RequestUsers = null });
            }
            keyboard.Add(row2);

            // Второй ряд: пользовательские команды
            var userCommands = GetQuickCommandsAsync(userId).Result;
            if (userCommands.Any())
            {
                var userRow = new List<KeyboardButton>();
                foreach (var uc in userCommands.Take(4))
                {
                    userRow.Add(new KeyboardButton(uc.Label) { RequestUsers = null });
                }
                keyboard.Add(userRow);
            }

            var markup = new ReplyKeyboardMarkup(keyboard)
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };

            return Task.FromResult(markup);
        }

        public Task<InlineKeyboardMarkup> GetInlineKeyboardAsync(long userId, string context)
        {
            // Inline-клавиатура для контекстных действий
            var buttons = new List<List<InlineKeyboardButton>>();

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("📋 Алиасы", "aliases_list"),
                InlineKeyboardButton.WithCallbackData("⏰ Задачи", "crons_list")
            });

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("➕ Добавить алиас", "alias_add"),
                InlineKeyboardButton.WithCallbackData("➕ Добавить задачу", "cron_add")
            });

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("❌ Закрыть", "close")
            });

            var markup = new InlineKeyboardMarkup(buttons);
            return Task.FromResult(markup);
        }

        public async Task AddQuickCommandAsync(long userId, string label, string command)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO QuickCommands (UserId, Label, Command)
                VALUES ($userId, $label, $command);
            ";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$label", label);
            cmd.Parameters.AddWithValue("$command", command);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("User {UserId}: добавлена быстрая команда {Label} → {Command}", userId, label, command);
        }

        public async Task RemoveQuickCommandAsync(long userId, string commandId)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM QuickCommands WHERE UserId = $userId AND Command = $command;";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$command", commandId);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("User {UserId}: удалена быстрая команда {CommandId}", userId, commandId);
        }

        public async Task<List<QuickCommand>> GetQuickCommandsAsync(long userId)
        {
            var commands = new List<QuickCommand>();
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Label, Command FROM QuickCommands WHERE UserId = $userId ORDER BY Id;";
            cmd.Parameters.AddWithValue("$userId", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                commands.Add(new QuickCommand
                {
                    Id = reader.GetInt64(0).ToString(),
                    Label = reader.GetString(1),
                    Command = reader.GetString(2),
                    UserId = userId
                });
            }

            return commands;
        }
    }
}
