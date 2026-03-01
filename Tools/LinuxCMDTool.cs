// File: Tools/LinuxCMDTool.cs
// Implementation of IToolFunction for executing safe Linux commands.
// This tool allows AI to perform limited system operations on Linux host,
// with strict security measures to prevent dangerous actions.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Tools
{
    /// <summary>
    /// Tool for executing safe Linux commands.
    /// Supports: view logs, read/edit/create/delete files, search, system info,
    /// service management, process control, network operations, archives, and more.
    ///
    /// Расширенная версия с поддержкой:
    /// - Работа с произвольными путями (не только в базовой директории)
    /// - Выполнение команд от имени суперпользователя через sudo (требует настройки)
    ///
    /// Security: Whitelist of commands from LinuxCmdAllowedActions, path restrictions, input sanitization.
    /// Register in DI as AddTransient<IToolFunction, LinuxCMDTool>().
    /// </summary>
    public class LinuxCMDTool : IToolFunction
    {
        public string Name => "LinuxCMD";

        public string Description =>
            "Execute Linux commands on the host system. " +
            "Supports: view_log, read_file, head_file, tail_file, grep, find, wc, sort, uniq, diff, " +
            "create_file, edit_file, delete_file, copy_file, move_file, list_dir, make_dir, remove_dir, " +
            "service_status, service_start, service_stop, service_restart, process_list, process_kill, " +
            "disk_usage, memory_usage, cpu_info, os_info, network_info, port_check, " +
            "run_script, run_python, tar_create, tar_extract, zip_create, zip_extract, " +
            "journalctl, dmesg, date, uptime, whoami, pwd, file_info, " +
            "ping, curl, wget, dns_lookup, env_list, env_get. " +
            "Supports sudo for privileged operations (see LinuxCmdAllowedActions.SudoAllowedActions). " +
            "Path restrictions apply by default, but can be extended for admin users (allow_any_path=true).";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string" },      // Действие из списка разрешённых
            { "path", "string" },        // Путь к файлу/директории
            { "pattern", "string" },     // Шаблон для grep/find
            { "content", "string" },     // Содержимое для записи
            { "options", "string" },     // Дополнительные опции
            { "use_sudo", "boolean" },   // Выполнять через sudo
            { "allow_any_path", "boolean" } // Разрешить работу вне базовой директории
        };

        private readonly ILogger<LinuxCMDTool> _logger;
        private readonly string _baseDir;
        private readonly List<string> _allowedDirs;
        private readonly HashSet<string> _allowedActions;
        private readonly HashSet<string> _bannedCommands;
        private readonly HashSet<string> _sudoAllowedActions;
        private readonly bool _allowSudo;

        public LinuxCMDTool(ILogger<LinuxCMDTool> logger, IConfiguration config)
        {
            _logger = logger;
            _baseDir = config["AppBaseDir"] ?? AppDomain.CurrentDomain.BaseDirectory;

            // Читаем разрешённые директории из конфига или используем значения по умолчанию
            var allowedDirsConfig = config["LinuxCMD:AllowedDirs"] ?? string.Join(",", LinuxCmdAllowedActions.DefaultDirectories);
            _allowedDirs = allowedDirsConfig.Split(',').Select(s => s.Trim()).ToList();

            // Читаем разрешённые действия из конфига или используем значения по умолчанию
            var allowedActionsConfig = config["LinuxCMD:AllowedActions"] ?? string.Join(",", LinuxCmdAllowedActions.DefaultActions);
            _allowedActions = allowedActionsConfig.Split(',').Select(s => s.Trim().ToLowerInvariant()).ToHashSet();

            // Читаем действия, разрешённые для sudo
            var sudoAllowedConfig = config["LinuxCMD:SudoAllowedActions"] ?? string.Join(",", LinuxCmdAllowedActions.SudoAllowedActions);
            _sudoAllowedActions = sudoAllowedConfig.Split(',').Select(s => s.Trim().ToLowerInvariant()).ToHashSet();

            // Разрешено ли sudo
            _allowSudo = bool.Parse(config["LinuxCMD:AllowSudo"] ?? "false");

            // Запрещённые команды (берём из конфигурации или значения по умолчанию)
            _bannedCommands = LinuxCmdAllowedActions.BannedCommands;

            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            foreach (var sub in _allowedDirs)
            {
                var dir = Path.Combine(_baseDir, sub);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
        }

        public async Task<string> ExecuteAsync(Dictionary<string, object> args)
        {
            // Логирование входных параметров
            _logger.LogInformation("LinuxCMD: Получены аргументы (count={Count}): {Args}", args.Count, JsonSerializer.Serialize(args));
            _logger.LogDebug("LinuxCMD: Разрешённые действия: {AllowedActions}", string.Join(", ", _allowedActions));

            // Отладка: логируем типы всех аргументов
            foreach (var kvp in args)
            {
                _logger.LogDebug("LinuxCMD: Аргумент '{Key}' тип={Type} значение={Value}", 
                    kvp.Key, kvp.Value?.GetType().Name, kvp.Value);
            }

            if (!args.TryGetValue("action", out var actionObj))
            {
                _logger.LogError("LinuxCMD: Ключ 'action' отсутствует в аргументах. Доступные ключи: {Keys}", string.Join(", ", args.Keys));
                return JsonSerializer.Serialize(new { error = "Missing 'action' parameter." });
            }

            string action;
            if (actionObj is string actionStr)
            {
                action = actionStr;
            }
            else if (actionObj is JsonElement jsonElem)
            {
                action = jsonElem.GetString() ?? string.Empty;
                _logger.LogDebug("LinuxCMD: action получен из JsonElement: {Action}", action);
            }
            else
            {
                action = actionObj?.ToString() ?? string.Empty;
                _logger.LogWarning("LinuxCMD: action имеет неожиданный тип {Type}, преобразовано в строку: {Action}", 
                    actionObj?.GetType().Name, action);
            }

            action = action.ToLowerInvariant();
            _logger.LogInformation("LinuxCMD: Запрошенное действие: {Action}", action);

            if (!_allowedActions.Contains(action))
            {
                _logger.LogWarning("LinuxCMD: Действие {Action} не найдено в разрешённых. Доступные: {AllowedActions}",
                    action, string.Join(", ", _allowedActions));
                return JsonSerializer.Serialize(new { error = "Invalid action. Supported: " + string.Join(", ", _allowedActions) });
            }

            // Извлечение строковых параметров с поддержкой разных типов
            string path = GetStringArg(args, "path");
            string pattern = GetStringArg(args, "pattern");
            string content = GetStringArg(args, "content");
            string options = GetStringArg(args, "options");
            bool useSudo = GetBoolArg(args, "use_sudo");
            bool allowAnyPath = GetBoolArg(args, "allow_any_path");

            _logger.LogDebug("LinuxCMD: path={Path}, pattern={Pattern}, content={Content}, options={Options}, useSudo={UseSudo}, allowAnyPath={AllowAnyPath}",
                path, pattern, content, options, useSudo, allowAnyPath);

            // Sanitize inputs
            path = SanitizePath(path);
            pattern = SanitizePattern(pattern);

            _logger.LogDebug("LinuxCMD: После санитизации: path={Path}, pattern={Pattern}", path, pattern);

            // Проверка sudo
            if (useSudo)
            {
                _logger.LogInformation("LinuxCMD: Проверка sudo для действия {Action}", action);
                if (!_allowSudo)
                {
                    _logger.LogWarning("LinuxCMD: Sudo запрещён в конфигурации");
                    return JsonSerializer.Serialize(new { error = "Sudo is disabled in configuration." });
                }
                if (!_sudoAllowedActions.Contains(action))
                {
                    _logger.LogWarning("LinuxCMD: Действие {Action} не разрешено для sudo", action);
                    return JsonSerializer.Serialize(new { error = $"Action '{action}' is not allowed with sudo." });
                }
            }

            // Проверка пути
            string fullPath;
            if (allowAnyPath)
            {
                _logger.LogInformation("LinuxCMD: Разрешён произвольный путь (allow_any_path=true)");
                if (path.StartsWith("/"))
                    fullPath = path;
                else
                    fullPath = Path.Combine(_baseDir, path);
            }
            else
            {
                // Стандартное поведение: только разрешённые директории
                if (string.IsNullOrEmpty(path))
                {
                    _logger.LogWarning("LinuxCMD: Пустой путь");
                    return JsonSerializer.Serialize(new { error = "Invalid path." });
                }

                fullPath = Path.Combine(_baseDir, path);
                _logger.LogDebug("LinuxCMD: Полный путь: {FullPath}", fullPath);
                
                if (!IsPathAllowed(fullPath))
                {
                    _logger.LogWarning("LinuxCMD: Доступ к пути запрещён: {Path}", fullPath);
                    return JsonSerializer.Serialize(new { error = "Access denied: Path outside allowed directories. Use allow_any_path=true for admin access." });
                }
            }

            try
            {
                string command = BuildCommand(action, fullPath, pattern, content, options, useSudo);
                if (string.IsNullOrEmpty(command))
                {
                    _logger.LogError("LinuxCMD: Не удалось построить команду для действия {Action}", action);
                    return JsonSerializer.Serialize(new { error = "Failed to build command." });
                }

                _logger.LogInformation("LinuxCMD: Построена команда: {Command}", command);
                _logger.LogInformation("LinuxCMD: Выполнение команды...");

                // Check for banned patterns
                if (_bannedCommands.Any(b => command.Contains(b, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogError("LinuxCMD: Запрещённая команда обнаружена: {Command}", command);
                    return JsonSerializer.Serialize(new { error = "Command contains banned operations." });
                }

                var output = await RunShellCommandAsync(command);
                _logger.LogInformation("LinuxCMD: Команда выполнена успешно. Вывод: {Output}", output);
                return JsonSerializer.Serialize(new { success = true, output = output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LinuxCMD: Ошибка выполнения команды для действия {Action}. Команда: {Command}", action, 
                    BuildCommand(action, fullPath, pattern, content, options, useSudo));
                return JsonSerializer.Serialize(new { error = "Execution failed: " + ex.Message });
            }
        }

        // ────────────────────────────────────────────────
        //                  Argument Helpers
        // ────────────────────────────────────────────────

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

        private static bool GetBoolArg(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var value))
                return false;

            return value switch
            {
                bool b => b,
                JsonElement je => je.GetBoolean(),
                int i => i != 0,
                double d => d != 0,
                string s => bool.Parse(s),
                _ => false
            };
        }

        // ────────────────────────────────────────────────
        //                  Security Helpers
        // ────────────────────────────────────────────────

        private string SanitizePath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // Remove dangerous chars/sequences
            input = Regex.Replace(input, @"[;&|><$()`\n\r]", string.Empty);

            // Нормализуем путь
            try
            {
                input = Path.GetFullPath(input).TrimStart(Path.DirectorySeparatorChar);
            }
            catch
            {
                // Если путь невалидный, возвращаем как есть
            }

            return input;
        }

        private string SanitizePattern(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            input = Regex.Replace(input, @"[;&|><$()`\n\r]", string.Empty);
            return input;
        }

        private bool IsPathAllowed(string fullPath)
        {
            // Check if path is under baseDir and in allowed subdirs
            if (!fullPath.StartsWith(_baseDir)) return false;
            var relative = fullPath.Substring(_baseDir.Length).TrimStart(Path.DirectorySeparatorChar);
            var subDir = relative.Split(Path.DirectorySeparatorChar).FirstOrDefault();
            return _allowedDirs.Contains(subDir ?? string.Empty);
        }

        // ────────────────────────────────────────────────
        //                  Command Builders
        // ────────────────────────────────────────────────

        private string BuildCommand(string action, string fullPath, string pattern, string content, string options, bool useSudo)
        {
            string sudo = useSudo ? "sudo " : "";

            return action switch
            {
                // Чтение и просмотр
                "view_log" => $"{sudo}tail {options} \"{fullPath}\"",
                "read_file" => $"{sudo}cat \"{fullPath}\"",
                "head_file" => $"{sudo}head {options} \"{fullPath}\"",
                "tail_file" => $"{sudo}tail {options} \"{fullPath}\"",
                "cat_file" => $"{sudo}cat \"{fullPath}\"",
                
                // Поиск и анализ
                "grep" => $"{sudo}grep {options} \"{EscapeContent(pattern)}\" \"{fullPath}\"",
                "find" => $"{sudo}find \"{Path.GetDirectoryName(fullPath) ?? _baseDir}\" {options} -name \"{EscapeContent(pattern)}\"",
                "wc" => $"{sudo}wc {options} \"{fullPath}\"",
                "sort" => $"{sudo}sort {options} \"{fullPath}\"",
                "uniq" => $"{sudo}uniq {options} \"{fullPath}\"",
                "diff" => $"{sudo}diff {options} \"{fullPath}\" \"{pattern}\"",
                
                // Файловые операции
                "create_file" => $"{sudo}touch \"{fullPath}\"",
                "edit_file" => $"{sudo}echo \"{EscapeContent(content)}\" >> \"{fullPath}\"",
                "delete_file" => $"{sudo}rm \"{fullPath}\"",
                "copy_file" => $"{sudo}cp {options} \"{fullPath}\" \"{pattern}\"",
                "move_file" => $"{sudo}mv {options} \"{fullPath}\" \"{pattern}\"",
                "list_dir" => $"{sudo}ls {options} \"{fullPath}\"",
                "make_dir" => $"{sudo}mkdir -p \"{fullPath}\"",
                "remove_dir" => $"{sudo}rmdir \"{fullPath}\"",
                
                // Системные команды
                "service_status" => $"{sudo}systemctl status {Path.GetFileNameWithoutExtension(fullPath)} || {sudo}ps aux | grep {Path.GetFileNameWithoutExtension(fullPath)}",
                "service_start" => $"{sudo}systemctl start {Path.GetFileNameWithoutExtension(fullPath)}",
                "service_stop" => $"{sudo}systemctl stop {Path.GetFileNameWithoutExtension(fullPath)}",
                "service_restart" => $"{sudo}systemctl restart {Path.GetFileNameWithoutExtension(fullPath)}",
                "process_list" => $"{sudo}ps aux {options}",
                "process_kill" => $"{sudo}kill {options} {Path.GetFileNameWithoutExtension(fullPath)}",
                "disk_usage" => $"{sudo}df {options} \"{fullPath}\"",
                "memory_usage" => $"{sudo}free {options}",
                "cpu_info" => $"{sudo}lscpu",
                "os_info" => $"{sudo}uname -a || {sudo}cat /etc/os-release",
                "network_info" => $"{sudo}ip addr {options} || {sudo}ifconfig {options}",
                "port_check" => $"{sudo}ss -tlnp {options} || {sudo}netstat -tlnp {options}",
                
                // Скрипты
                "run_script" => $"{sudo}bash \"{fullPath}\" {options}",
                "run_python" => $"{sudo}python3 \"{fullPath}\" {options}",
                
                // Архивы
                "tar_create" => $"{sudo}tar czf \"{pattern}\" \"{fullPath}\"",
                "tar_extract" => $"{sudo}tar xzf \"{fullPath}\" {options}",
                "zip_create" => $"{sudo}zip {options} \"{pattern}\" \"{fullPath}\"",
                "zip_extract" => $"{sudo}unzip {options} \"{fullPath}\"",
                
                // Логи и журналирование
                "journalctl" => $"{sudo}journalctl {options}",
                "dmesg" => $"{sudo}dmesg {options}",
                
                // Дата и время
                "date" => $"{sudo}date {options}",
                "uptime" => $"{sudo}uptime",
                
                // Пользователи и права
                "whoami" => "whoami",
                "pwd" => "pwd",
                "file_info" => $"{sudo}stat \"{fullPath}\" || {sudo}file \"{fullPath}\"",
                
                // Сеть
                "ping" => $"ping {options} \"{pattern}\"",
                "curl" => $"curl {options} \"{pattern}\"",
                "wget" => $"wget {options} \"{pattern}\"",
                "dns_lookup" => $"{sudo}dig {pattern} || {sudo}nslookup {pattern}",
                
                // Переменные окружения
                "env_list" => $"{sudo}env || {sudo}printenv",
                "env_get" => $"{sudo}echo ${pattern}",
                
                _ => string.Empty
            };
        }

        private string EscapeContent(string content)
        {
            return content.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
        }

        // ────────────────────────────────────────────────
        //                  Execution
        // ────────────────────────────────────────────────

        private async Task<string> RunShellCommandAsync(string command)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
                throw new Exception($"Command failed (exit {process.ExitCode}): {error}");

            return output.Trim();
        }
    }
}
