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
using AgentBot.Security;

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
            "create_file, edit_file (append), write_file (overwrite), replace_in_file (sed find/replace), insert_line (insert at line#), " +
            "delete_file, copy_file, move_file, list_dir, make_dir, remove_dir, " +
            "service_status, service_start, service_stop, service_restart, process_list, process_kill, " +
            "disk_usage, memory_usage, cpu_info, os_info, network_info, port_check, " +
            "run_script, run_python, tar_create, tar_extract, zip_create, zip_extract, " +
            "journalctl, dmesg, date, uptime, whoami, pwd, file_info, " +
            "ping, curl, wget, dns_lookup, env_list, env_get. " +
            "FILE EDITING: Use 'write_file' to overwrite a file entirely (content=new text). " +
            "Use 'replace_in_file' to find/replace text (pattern=old text, content=new text, options='g' for all occurrences). " +
            "Use 'insert_line' to insert text at a line number (content=text, options=line_number). " +
            "Use 'edit_file' to append text to the end of a file (content=text to append). " +
            "IMPORTANT: For absolute paths (e.g., /etc, /var/log) or system-wide operations, set allow_any_path=true. " +
            "For sudo operations (service management, system logs), set use_sudo=true. " +
            "Admin users have extended privileges - check user role with /whoami command before using allow_any_path or use_sudo. " +
            "By default (allow_any_path=false), you can ONLY access files in allowed subdirectories (like logs/, data/, config/). " +
            "To access files in the root directory (like appsettings.json), you MUST set allow_any_path=true. " +
            "DO NOT prepend 'app/' or other base directory - the tool handles path resolution automatically.";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string" },      // Действие из списка разрешённых
            { "path", "string" },        // Путь к файлу/директории: для относительных путей используй только имя файла или путь относительно текущей директории (например, 'appsettings.json', 'logs/app.log'). НЕ добавляй 'app/' или другие префиксы.
            { "pattern", "string" },     // Шаблон для grep/find
            { "content", "string" },     // Содержимое для записи
            { "options", "string" },     // Дополнительные опции
            { "use_sudo", "boolean" },   // Выполнять через sudo (только для администраторов, проверьте через /whoami)
            { "allow_any_path", "boolean" } // Разрешить работу вне базовой директории (только для администраторов)
        };

        private readonly ILogger<LinuxCMDTool> _logger;
        private readonly string _baseDir;
        private readonly List<string> _allowedDirs;
        private readonly HashSet<string> _allowedActions;
        private readonly HashSet<string> _bannedCommands;
        private readonly HashSet<string> _sudoAllowedActions;
        private readonly bool _allowSudo;
        private readonly AccessControlService _accessControl;

        public LinuxCMDTool(
            ILogger<LinuxCMDTool> logger,
            IConfiguration config,
            AccessControlService accessControl)
        {
            _logger = logger;
            
            // _baseDir используется только для проверки разрешённых директорий
            // Для относительных путей используется текущая рабочая директория (где запущен процесс)
            _baseDir = AppContext.BaseDirectory;
            
            _logger.LogInformation("LinuxCMDTool: _baseDir={BaseDir}, CurrentDirectory={CurrentDir}",
                _baseDir, Environment.CurrentDirectory);

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

            _accessControl = accessControl ?? throw new ArgumentNullException(nameof(accessControl));

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

        public async Task<string> ExecuteAsync(Dictionary<string, object> args, long chatId = default)
        {
            // Логирование входных параметров
            _logger.LogInformation("LinuxCMD: Получены аргументы (count={Count}) от chatId={ChatId}: {Args}", args.Count, chatId, JsonSerializer.Serialize(args));
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

            // Проверка sudo - ТОЛЬКО для администраторов
            if (useSudo)
            {
                _logger.LogInformation("LinuxCMD: Проверка sudo для действия {Action} от chatId={ChatId}", action, chatId);
                
                if (!_allowSudo)
                {
                    _logger.LogWarning("LinuxCMD: Sudo запрещён в конфигурации");
                    return JsonSerializer.Serialize(new { error = "Sudo is disabled in configuration." });
                }

                // Проверка прав администратора
                if (!_accessControl.IsAdmin(chatId))
                {
                    _logger.LogWarning("LinuxCMD: Пользователь chatId={ChatId} не является администратором, sudo запрещён", chatId);
                    return JsonSerializer.Serialize(new { error = "Sudo access denied: admin privileges required. Use /register to become an admin." });
                }

                if (!_sudoAllowedActions.Contains(action))
                {
                    _logger.LogWarning("LinuxCMD: Действие {Action} не разрешено для sudo", action);
                    return JsonSerializer.Serialize(new { error = $"Action '{action}' is not allowed with sudo." });
                }
                
                _logger.LogInformation("LinuxCMD: Пользователь chatId={ChatId} подтверждён как администратор, sudo разрешён", chatId);
            }

            // Проверка пути - для не-администраторов только разрешённые директории
            string fullPath;
            if (allowAnyPath)
            {
                _logger.LogInformation("LinuxCMD: Запрошен произвольный путь (allow_any_path=true) от chatId={ChatId}", chatId);

                // Проверка прав администратора для доступа к произвольным путям
                if (!_accessControl.IsAdmin(chatId))
                {
                    _logger.LogWarning("LinuxCMD: Пользователь chatId={ChatId} не является администратором, доступ к произвольным путям запрещён", chatId);
                    return JsonSerializer.Serialize(new { error = "Arbitrary path access denied: admin privileges required. Use /register to become an admin." });
                }

                _logger.LogInformation("LinuxCMD: Администратор chatId={ChatId} получает доступ к произвольному пути", chatId);
                // Для абсолютных путей используем как есть, для относительных - используем текущую рабочую директорию
                if (path.StartsWith("/"))
                {
                    fullPath = path;
                    _logger.LogDebug("LinuxCMD: allow_any_path: абсолютный путь path={Path}", fullPath);
                }
                else
                {
                    // Для относительных путей используем как есть (рабочая директория = /app)
                    fullPath = path;
                    _logger.LogDebug("LinuxCMD: allow_any_path: относительный путь path={Path}, будет использован относительно CurrentDirectory={CurrentDir}",
                        path, Environment.CurrentDirectory);
                }
            }
            else
            {
                // Стандартное поведение: только разрешённые директории
                if (string.IsNullOrEmpty(path))
                {
                    _logger.LogWarning("LinuxCMD: Пустой путь");
                    return JsonSerializer.Serialize(new { error = "Invalid path." });
                }

                // Для обычных пользователей абсолютные пути запрещены
                if (path.StartsWith("/"))
                {
                    _logger.LogWarning("LinuxCMD: Абсолютный путь запрещён для не-администраторов: {Path}", path);
                    return JsonSerializer.Serialize(new { error = "Absolute paths are not allowed. Use relative paths in allowed directories." });
                }

                // Для относительных путей используем как есть (рабочая директория = /app)
                fullPath = path;
                _logger.LogDebug("LinuxCMD: Полный путь: {FullPath} (будет использован относительно CurrentDirectory={CurrentDir})", fullPath, Environment.CurrentDirectory);

                // Проверка по поддиректории (для относительных путей)
                var parts = path.Split('/', '\\');
                var subDir = parts.Length > 1 ? parts[0] : string.Empty;
                
                if (string.IsNullOrEmpty(subDir))
                {
                    _logger.LogWarning("LinuxCMD: Доступ к корневой директории запрещен (требуется allow_any_path=true): {Path}", path);
                    return JsonSerializer.Serialize(new { error = $"Access to files in the root directory (like '{path}') requires 'allow_any_path': true." });
                }

                if (!_allowedDirs.Contains(subDir))
                {
                    _logger.LogWarning("LinuxCMD: Доступ к директории запрещён: {SubDir}", subDir);
                    return JsonSerializer.Serialize(new { error = $"Access denied: directory '{subDir}' is not in allowed directories ({string.Join(", ", _allowedDirs)})." });
                }
            }

            try
            {
                // Для действий с файлами проверим существование файла
                if (action is "read_file" or "view_log" or "head_file" or "tail_file" or "cat_file")
                {
                    // Проверяем существование файла
                    string fileToCheck = fullPath.StartsWith("/") ? fullPath : Path.Combine(Environment.CurrentDirectory, fullPath);
                    
                    if (!File.Exists(fileToCheck))
                    {
                        _logger.LogError("LinuxCMD: Файл не найден: {Path} (полный путь={FullPath}, CurrentDir={CurrentDir})",
                            path, fileToCheck, Environment.CurrentDirectory);
                        return JsonSerializer.Serialize(new { error = $"File not found: {path}" });
                    }
                    
                    _logger.LogDebug("LinuxCMD: Файл найден: {FullPath}", fileToCheck);
                }

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

            // Удаляем распространённые ошибочные префиксы, которые может добавлять ИИ
            // Например, 'app/', './app/', '../app/' и т.д.
            input = Regex.Replace(input, @"^(\./|\.\./|app/)+", string.Empty, RegexOptions.IgnoreCase);
            
            // Нормализуем путь
            try
            {
                bool isAbsolute = Path.IsPathRooted(input);
                string normalizedFullPath = Path.GetFullPath(input);
                if (!isAbsolute)
                {
                    // Если путь был относительным, нужно вернуть его относительным текущей директории
                    input = Path.GetRelativePath(Environment.CurrentDirectory, normalizedFullPath);
                    input = input.Replace('\\', '/'); // Унифицируем слеши
                }
                else
                {
                    input = normalizedFullPath.Replace('\\', '/');
                }
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
                "find" => $"{sudo}find \"{(string.IsNullOrEmpty(fullPath) ? _baseDir : fullPath)}\" {options} -name \"{EscapeContent(pattern)}\"",
                "wc" => $"{sudo}wc {options} \"{fullPath}\"",
                "sort" => $"{sudo}sort {options} \"{fullPath}\"",
                "uniq" => $"{sudo}uniq {options} \"{fullPath}\"",
                "diff" => $"{sudo}diff {options} \"{fullPath}\" \"{pattern}\"",
                
                // Файловые операции
                    "create_file" => $"{sudo}touch \"{fullPath}\"",
                    "edit_file" => $"{sudo}echo \"{EscapeContent(content)}\" >> \"{fullPath}\"",
                    "write_file" => BuildWriteFileCommand(sudo, fullPath, content),
                    "replace_in_file" => BuildReplaceInFileCommand(sudo, fullPath, pattern, content, options),
                    "insert_line" => BuildInsertLineCommand(sudo, fullPath, content, options),
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
                "run_script" => BuildRunScriptCommand(sudo, fullPath, content, options),
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

        /// <summary>
        /// Перезаписывает файл целиком, используя heredoc через tee.
        /// </summary>
        private static string BuildWriteFileCommand(string sudo, string fullPath, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return $"{sudo}truncate -s 0 \"{fullPath}\"";

            // Используем printf + tee для безопасной записи (без интерпретации спецсимволов)
            return $"printf '%s' '{EscapeSingleQuoted(content)}' | {sudo}tee \"{fullPath}\" > /dev/null";
        }

        /// <summary>
        /// Заменяет первое или все вхождения подстроки в файле через sed.
        /// pattern = искомая строка, content = замена, options может содержать "g" для замены всех вхождений.
        /// </summary>
        private static string BuildReplaceInFileCommand(string sudo, string fullPath, string pattern, string content, string options)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "echo 'Error: pattern (old text) is required for replace_in_file'";

            // Экранируем разделитель sed и спецсимволы
            string escapedPattern = EscapeSed(pattern);
            string escapedContent = EscapeSed(content);
            string flags = options?.Contains("g", System.StringComparison.OrdinalIgnoreCase) == true ? "g" : "";

            return $"{sudo}sed -i 's/{escapedPattern}/{escapedContent}/{flags}' \"{fullPath}\"";
        }

        /// <summary>
        /// Вставляет строку текста перед указанным номером строки через sed.
        /// options = номер строки (например "5"), content = текст для вставки.
        /// </summary>
        private static string BuildInsertLineCommand(string sudo, string fullPath, string content, string options)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "echo 'Error: content is required for insert_line'";

            if (!int.TryParse(options?.Trim(), out int lineNumber) || lineNumber < 1)
                return "echo 'Error: options must contain a valid line number (>= 1) for insert_line'";

            string escapedContent = EscapeSed(content);
            return $"{sudo}sed -i '{lineNumber}i\\{escapedContent}' \"{fullPath}\"";
        }

        /// <summary>
        /// Экранирует спецсимволы для использования внутри sed выражения.
        /// </summary>
        private static string EscapeSed(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // Экранируем: / \ & и символ новой строки
            return input
                .Replace("\\", "\\\\")
                .Replace("/", "\\/")
                .Replace("&", "\\&")
                .Replace("\n", "\\n");
        }

        private static string BuildRunScriptCommand(string sudo, string fullPath, string content, string options)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                return $"{sudo}bash -lc '{EscapeSingleQuoted(content)}'";
            }

            if (!string.IsNullOrWhiteSpace(fullPath))
            {
                return $"{sudo}bash \"{fullPath}\" {options}";
            }

            return string.Empty;
        }

        private static string EscapeSingleQuoted(string value)
        {
            return value.Replace("'", "'\\''");
        }

        // ────────────────────────────────────────────────
        //                  Execution
        // ────────────────────────────────────────────────

        private async Task<string> RunShellCommandAsync(string command)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory // Явно указываем рабочую директорию
            };

            processInfo.ArgumentList.Add("-c");
            processInfo.ArgumentList.Add(command);

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
