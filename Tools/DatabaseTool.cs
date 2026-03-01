using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace AgentBot.Tools
{
    public static class DatabaseHelper
    {
        private static string _connectionString;

        public static void Initialize(IConfiguration configuration)
        {
            string dbPath = Path.Combine(configuration["AppBaseDir"] ?? ".", "notes.db");
            _connectionString = $"Data Source={dbPath}";

            using var connection = GetConnection();
            connection.Open();

            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS Notes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );";

            using var command = new SqliteCommand(createTableQuery, connection);
            command.ExecuteNonQuery();
        }

        public static SqliteConnection GetConnection()
        {
            if (_connectionString == null)
            {
                _connectionString = "Data Source=notes.db";
                Initialize(null!); // fallback
            }
            return new SqliteConnection(_connectionString);
        }
    }

    public class SaveNoteTool : IToolFunction
    {
        public SaveNoteTool(IConfiguration config) => DatabaseHelper.Initialize(config);
        
        public string Name => "save_note";
        public string Description => "Save a note for a user in the database. Returns the ID of the created note.";

        public Dictionary<string, string> Parameters => new()
        {
            { "user_id", "string" },
            { "content", "string" }
        };

        public async Task<string> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("user_id", out var userIdObj) || !args.TryGetValue("content", out var contentObj))
                return JsonSerializer.Serialize(new { error = "Missing user_id or content." });

            string userId = userIdObj.ToString()!;
            string content = contentObj.ToString()!;

            using var connection = DatabaseHelper.GetConnection();
            await connection.OpenAsync();

            string query = "INSERT INTO Notes (UserId, Content) VALUES (@UserId, @Content); SELECT last_insert_rowid();";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Content", content);

            var result = await command.ExecuteScalarAsync();
            return JsonSerializer.Serialize(new { id = result, status = "Note saved successfully" });
        }
    }

    public class GetNoteTool : IToolFunction
    {
        public GetNoteTool(IConfiguration config) => DatabaseHelper.Initialize(config);
        
        public string Name => "get_note";
        public string Description => "Retrieve a specific note by ID for a user.";

        public Dictionary<string, string> Parameters => new()
        {
            { "user_id", "string" },
            { "note_id", "number" }
        };

        public async Task<string> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("user_id", out var userIdObj) || !args.TryGetValue("note_id", out var noteIdObj))
                return JsonSerializer.Serialize(new { error = "Missing user_id or note_id." });

            string userId = userIdObj.ToString()!;
            if (!long.TryParse(noteIdObj.ToString(), out long noteId))
                return JsonSerializer.Serialize(new { error = "Invalid note_id." });

            using var connection = DatabaseHelper.GetConnection();
            await connection.OpenAsync();

            string query = "SELECT Id, Content, CreatedAt FROM Notes WHERE Id = @Id AND UserId = @UserId";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", noteId);
            command.Parameters.AddWithValue("@UserId", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var note = new
                {
                    id = reader.GetInt64(0),
                    content = reader.GetString(1),
                    created_at = reader.GetDateTime(2)
                };
                return JsonSerializer.Serialize(note);
            }

            return JsonSerializer.Serialize(new { error = "Note not found." });
        }
    }

    public class ListNotesTool : IToolFunction
    {
        public ListNotesTool(IConfiguration config) => DatabaseHelper.Initialize(config);
        
        public string Name => "list_notes";
        public string Description => "List all notes for a specific user.";

        public Dictionary<string, string> Parameters => new()
        {
            { "user_id", "string" }
        };

        public async Task<string> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("user_id", out var userIdObj))
                return JsonSerializer.Serialize(new { error = "Missing user_id." });

            string userId = userIdObj.ToString()!;

            using var connection = DatabaseHelper.GetConnection();
            await connection.OpenAsync();

            string query = "SELECT Id, Content, CreatedAt FROM Notes WHERE UserId = @UserId ORDER BY CreatedAt DESC";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            using var reader = await command.ExecuteReaderAsync();
            var notes = new List<object>();

            while (await reader.ReadAsync())
            {
                notes.Add(new
                {
                    id = reader.GetInt64(0),
                    content = reader.GetString(1),
                    created_at = reader.GetDateTime(2)
                });
            }

            return JsonSerializer.Serialize(notes);
        }
    }

    public class DeleteNoteTool : IToolFunction
    {
        public DeleteNoteTool(IConfiguration config) => DatabaseHelper.Initialize(config);
        
        public string Name => "delete_note";
        public string Description => "Delete a specific note by ID for a user.";

        public Dictionary<string, string> Parameters => new()
        {
            { "user_id", "string" },
            { "note_id", "number" }
        };

        public async Task<string> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("user_id", out var userIdObj) || !args.TryGetValue("note_id", out var noteIdObj))
                return JsonSerializer.Serialize(new { error = "Missing user_id or note_id." });

            string userId = userIdObj.ToString()!;
            if (!long.TryParse(noteIdObj.ToString(), out long noteId))
                return JsonSerializer.Serialize(new { error = "Invalid note_id." });

            using var connection = DatabaseHelper.GetConnection();
            await connection.OpenAsync();

            string query = "DELETE FROM Notes WHERE Id = @Id AND UserId = @UserId";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", noteId);
            command.Parameters.AddWithValue("@UserId", userId);

            int rows = await command.ExecuteNonQueryAsync();
            if (rows > 0)
                return JsonSerializer.Serialize(new { status = "Note deleted successfully" });
            
            return JsonSerializer.Serialize(new { error = "Note not found or already deleted." });
        }
    }
}
