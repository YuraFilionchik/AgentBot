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
    /// Supports: view logs (tail/cat), check service status (systemctl/ps), run scripts (bash),
    /// edit files (echo/sed), create files (touch/echo), delete files (rm),
    /// search in files (grep), find files/directories (find).
    /// 
    /// Расширенная версия с поддержкой:
    /// - Работа с произвольными путями (не только в базовой директории)
    /// - Выполнение команд от имени суперпользователя через sudo (требует настройки)
    /// 
    /// Security: Whitelist of commands, path restrictions, input sanitization.
    /// Register in DI as AddTransient<IToolFunction, LinuxCMDTool>().
    /// </summary>
    public class LinuxCMDTool : IToolFunction
    {
        public string Name => "LinuxCMD";

        public string Description =>
            "Execute Linux commands on the host system. " +
            "Supports: view logs (tail/cat), check service status (systemctl/ps), run scripts (bash), " +
            "edit files (echo/sed), create files (touch/echo), delete files (rm), " +
            "search in files (grep), find files/directories (find). " +
            "Supports sudo for privileged operations (configure SudoAllowedActions in appsettings.json). " +
            "Path restrictions apply by default, but can be extended for admin users.";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string" },
            { "path", "string" },
            { "pattern", "string" },
            { "content", "string" },
            { "options", "string" },
            { "use_sudo", "boolean" },      // Выполнять через sudo
            { "allow_any_path", "boolean" } // Разрешить работу вне базовой директории
        };

        private readonly ILogger<LinuxCMDTool> _logger;
        private readonly string _baseDir;
        private readonly List<string> _allowedDirs;
        private readonly HashSet<string> _allowedActions;
        private readonly HashSet<string> _bannedCommands;
        private readonly HashSet<string> _sudoAllowedActions; // Действия, разрешённые для sudo
        private readonly bool _allowSudo; // Разрешено ли sudo вообще

        public LinuxCMDTool(ILogger<LinuxCMDTool> logger, IConfiguration config)
        {
            _logger = logger;
            _baseDir = config["AppBaseDir"] ?? AppDomain.CurrentDomain.BaseDirectory;
            
            // Читаем разрешённые директории из конфига
            var allowedDirsConfig = config["LinuxCMD:AllowedDirs"] ?? "logs,scripts,data";
            _allowedDirs = allowedDirsConfig.Split(',').Select(s => s.Trim()).ToList();

            // Читаем разрешённые действия
            var allowedActionsConfig = config["LinuxCMD:AllowedActions"] ?? 
                "view_log,service_status,run_script,edit_file,create_file,delete_file,grep,find";
            _allowedActions = allowedActionsConfig.Split(',').Select(s => s.Trim().ToLowerInvariant()).ToHashSet();

            // Читаем действия, разрешённые для sudo
            var sudoAllowedConfig = config["LinuxCMD:SudoAllowedActions"] ?? "service_status,run_script";
            _sudoAllowedActions = sudoAllowedConfig.Split(',').Select(s => s.Trim().ToLowerInvariant()).ToHashSet();

            // Разрешено ли sudo
            _allowSudo = bool.Parse(config["LinuxCMD:AllowSudo"] ?? "false");

            // Запрещённые команды (никогда не разрешены)
            _bannedCommands = new HashSet<string>
            {
                "rm -rf /", "rm -rf /*", "dd if=/dev/zero", "mkfs", 
                "shutdown", "reboot", "halt", "poweroff",
                "apt-get remove --purge .*", "yum erase .*",
                "chmod -R 777 /", "chown -R root:root /"
            };

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
            if (!args.TryGetValue("action", out var actionObj) || actionObj is not string action || !_allowedActions.Contains(action.ToLowerInvariant()))
            {
                _logger.LogWarning("Invalid or unsupported action: {Action}", actionObj);
                return JsonSerializer.Serialize(new { error = "Invalid action. Supported: " + string.Join(", ", _allowedActions) });
            }

            string path = args.TryGetValue("path", out var pathObj) && pathObj is string p ? p : string.Empty;
            string pattern = args.TryGetValue("pattern", out var patternObj) && patternObj is string pt ? pt : string.Empty;
            string content = args.TryGetValue("content", out var contentObj) && contentObj is string c ? c : string.Empty;
            string options = args.TryGetValue("options", out var optsObj) && optsObj is string o ? o : string.Empty;
            bool useSudo = args.TryGetValue("use_sudo", out var sudoObj) && (bool)(sudoObj ?? false);
            bool allowAnyPath = args.TryGetValue("allow_any_path", out var pathObj2) && (bool)(pathObj2 ?? false);

            // Sanitize inputs
            path = SanitizePath(path);
            pattern = SanitizePattern(pattern);

            // Проверка sudo
            if (useSudo)
            {
                if (!_allowSudo)
                {
                    return JsonSerializer.Serialize(new { error = "Sudo is disabled in configuration." });
                }
                if (!_sudoAllowedActions.Contains(action.ToLowerInvariant()))
                {
                    return JsonSerializer.Serialize(new { error = $"Action '{action}' is not allowed with sudo." });
                }
            }

            // Проверка пути
            string fullPath;
            if (allowAnyPath)
            {
                // Для администраторов: разрешаем любые пути, но с проверкой на опасные паттерны
                if (path.StartsWith("/"))
                    fullPath = path;
                else
                    fullPath = Path.Combine(_baseDir, path);
            }
            else
            {
                // Стандартное поведение: только разрешённые директории
                if (string.IsNullOrEmpty(path))
                    return JsonSerializer.Serialize(new { error = "Invalid path." });

                fullPath = Path.Combine(_baseDir, path);
                if (!IsPathAllowed(fullPath))
                {
                    _logger.LogWarning("Access denied to path: {Path}", fullPath);
                    return JsonSerializer.Serialize(new { error = "Access denied: Path outside allowed directories. Use allow_any_path=true for admin access." });
                }
            }

            try
            {
                string command = BuildCommand(action.ToLowerInvariant(), fullPath, pattern, content, options, useSudo);
                if (string.IsNullOrEmpty(command))
                    return JsonSerializer.Serialize(new { error = "Failed to build command." });

                // Check for banned patterns
                if (_bannedCommands.Any(b => command.Contains(b, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogError("Banned command detected: {Command}", command);
                    return JsonSerializer.Serialize(new { error = "Command contains banned operations." });
                }

                _logger.LogInformation("Executing command: {Command}", command);

                var output = await RunShellCommandAsync(command);
                return JsonSerializer.Serialize(new { success = true, output = output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing Linux command for action {Action}", action);
                return JsonSerializer.Serialize(new { error = "Execution failed: " + ex.Message });
            }
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
                "view_log" => $"{sudo}tail {options} \"{fullPath}\"",
                "service_status" => $"{sudo}systemctl status {Path.GetFileNameWithoutExtension(fullPath)} || {sudo}ps aux | grep {Path.GetFileNameWithoutExtension(fullPath)}",
                "run_script" => $"{sudo}bash \"{fullPath}\" {options}",
                "create_file" => $"{sudo}touch \"{fullPath}\" || echo \"{EscapeContent(content)}\" {sudo}> \"{fullPath}\"",
                "edit_file" => $"{sudo}echo \"{EscapeContent(content)}\" >> \"{fullPath}\"",
                "delete_file" => $"{sudo}rm \"{fullPath}\"",
                "grep" => $"{sudo}grep {options} \"{EscapeContent(pattern)}\" \"{fullPath}\"",
                "find" => $"{sudo}find \"{Path.GetDirectoryName(fullPath) ?? _baseDir}\" {options} -name \"{EscapeContent(pattern)}\"",
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
