using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Security
{
    /// <summary>
    /// Сервис управления правами доступа.
    /// Администраторы регистрируются командой /register &lt;пароль&gt;.
    /// Список администраторов хранится в JSON-файле на диске.
    /// </summary>
    public class AccessControlService
    {
        private readonly ILogger<AccessControlService> _logger;
        private readonly string _adminPassword;
        private readonly string _adminListPath;

        // chatId → имя пользователя (для логов)
        private readonly HashSet<long> _adminChatIds = new();
        private readonly SemaphoreSlim _lock = new(1, 1);

        public AccessControlService(
            IConfiguration configuration,
            ILogger<AccessControlService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _adminPassword = configuration["Security:AdminPassword"]
                ?? throw new ArgumentException("Security:AdminPassword не задан в appsettings.json");

            string baseDir = configuration["AppBaseDir"] ?? AppDomain.CurrentDomain.BaseDirectory;
            _adminListPath = Path.Combine(baseDir, "admins.json");

            // Загружаем сохранённых администраторов с диска
            LoadAdminsFromDisk();

            // Опциональный список начальных администраторов из конфигурации
            var initialAdmins = configuration.GetSection("Security:InitialAdminIds").Get<List<long>>();
            if (initialAdmins != null)
            {
                foreach (var id in initialAdmins)
                    _adminChatIds.Add(id);
                SaveAdminsToDisk();
            }

            _logger.LogInformation("AccessControlService запущен. Администраторов: {Count}", _adminChatIds.Count);
        }

        // ────────────────────────────────────────────────
        //  Публичное API
        // ────────────────────────────────────────────────

        /// <summary>
        /// Проверяет, является ли пользователь администратором.
        /// </summary>
        public bool IsAdmin(long chatId) => _adminChatIds.Contains(chatId);

        /// <summary>
        /// Пытается зарегистрировать chatId как администратора.
        /// Возвращает true, если пароль верный (и регистрация прошла — или пользователь уже был).
        /// </summary>
        public async Task<RegisterResult> TryRegisterAdminAsync(long chatId, string password)
        {
            if (IsAdmin(chatId))
                return RegisterResult.AlreadyAdmin;

            if (!string.Equals(password, _adminPassword, StringComparison.Ordinal))
            {
                _logger.LogWarning("Неверный пароль при попытке регистрации администратора от chatId={ChatId}", chatId);
                return RegisterResult.WrongPassword;
            }

            await _lock.WaitAsync();
            try
            {
                _adminChatIds.Add(chatId);
                SaveAdminsToDisk();
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation("Новый администратор зарегистрирован: chatId={ChatId}", chatId);
            return RegisterResult.Success;
        }

        /// <summary>
        /// Удаляет chatId из списка администраторов.
        /// </summary>
        public async Task<bool> RevokeAdminAsync(long chatId)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_adminChatIds.Remove(chatId)) return false;
                SaveAdminsToDisk();
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation("Администратор удалён: chatId={ChatId}", chatId);
            return true;
        }

        /// <summary>
        /// Возвращает количество администраторов.
        /// </summary>
        public int AdminCount => _adminChatIds.Count;

        // ────────────────────────────────────────────────
        //  Персистентность
        // ────────────────────────────────────────────────

        private void LoadAdminsFromDisk()
        {
            try
            {
                if (!File.Exists(_adminListPath)) return;

                string json = File.ReadAllText(_adminListPath);
                var ids = JsonSerializer.Deserialize<List<long>>(json);
                if (ids != null)
                    foreach (var id in ids)
                        _adminChatIds.Add(id);

                _logger.LogDebug("Загружено {Count} администраторов из {Path}", _adminChatIds.Count, _adminListPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при загрузке списка администраторов из {Path}", _adminListPath);
            }
        }

        private void SaveAdminsToDisk()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_adminListPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(new List<long>(_adminChatIds), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_adminListPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении списка администраторов в {Path}", _adminListPath);
            }
        }
    }

    public enum RegisterResult
    {
        Success,
        AlreadyAdmin,
        WrongPassword
    }
}
