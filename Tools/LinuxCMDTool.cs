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
    /// Security: Whitelist of commands, path restrictions, no sudo/root, input sanitization.
    /// Register in DI as AddTransient<IToolFunction, LinuxCMDTool>().
    /// </summary>
    public class LinuxCMDTool : IToolFunction
    {
        public string Name => "LinuxCMD";

        public string Description =>
            "Execute safe Linux commands on the host system. " +
            "Supports: view logs (tail/cat), check service status (systemctl/ps), run scripts (bash), " +
            "edit files (echo/sed), create files (touch/echo), delete files (rm), " +
            "search in files (grep), find files/directories (find). " +
            "All operations restricted to safe directories (e.g., /app/logs, /app/scripts). " +
            "Do not use for destructive or privileged actions.";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string" }, // Enum-like: "view_log", "service_status", "run_script", "edit_file", "create_file", "delete_file", "grep", "find"
            { "path", "string" },   // File/path (relative or absolute, but sanitized)
            { "pattern", "string" }, // For grep/find: search pattern
            { "content", "string" }, // For edit/create (new content)
            { "options", "string" }  // Additional opts (e.g., "-n 10" for tail, "-i" for grep)
        };

        private readonly ILogger<LinuxCMDTool> _logger;
        private readonly string _baseDir; // e.g., from config: "/app" or AppDomain.BaseDirectory
        private readonly List<string> _allowedDirs = new() { "logs", "scripts", "data" }; // Subdirs under base
        private readonly HashSet<string> _allowedActions = new()
        {
            "view_log", "service_status", "run_script", "edit_file", "create_file", "delete_file", "grep", "find"
        };
        private readonly HashSet<string> _bannedCommands = new()
        {
            "sudo", "rm -rf", "dd", "mkfs", "shutdown", "reboot", "apt", "yum", "pip", "wget", "curl" // etc.
        };

        public LinuxCMDTool(ILogger<LinuxCMDTool> logger, IConfiguration config)
        {
            _logger = logger;
            _baseDir = config["AppBaseDir"] ?? AppDomain.CurrentDomain.BaseDirectory; // From appsettings.json
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
                return JsonSerializer.Serialize(new { error = "Invalid action. Supported: view_log, service_status, run_script, edit_file, create_file, delete_file, grep, find." });
            }

            string path = args.TryGetValue("path", out var pathObj) && pathObj is string p ? p : string.Empty;
            string pattern = args.TryGetValue("pattern", out var patternObj) && patternObj is string pt ? pt : string.Empty;
            string content = args.TryGetValue("content", out var contentObj) && contentObj is string c ? c : string.Empty;
            string options = args.TryGetValue("options", out var optsObj) && optsObj is string o ? o : string.Empty;

            // Sanitize inputs
            path = SanitizePath(path);
            pattern = SanitizePattern(pattern); // Additional for grep/find
            if (string.IsNullOrEmpty(path))
                return JsonSerializer.Serialize(new { error = "Invalid path." });

            string fullPath = Path.Combine(_baseDir, path);
            if (!IsPathAllowed(fullPath))
            {
                _logger.LogWarning("Access denied to path: {Path}", fullPath);
                return JsonSerializer.Serialize(new { error = "Access denied: Path outside allowed directories." });
            }

            try
            {
                string command = BuildCommand(action.ToLowerInvariant(), fullPath, pattern, content, options);
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
            input = Regex.Replace(input, @"[;&|><$()`\n\r]", string.Empty); // Prevent injection
            input = Path.GetFullPath(input).TrimStart(Path.DirectorySeparatorChar); // Normalize, no absolute root
            return input;
        }

        private string SanitizePattern(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // For grep/find: remove dangerous chars, limit to safe patterns
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

        private string BuildCommand(string action, string fullPath, string pattern, string content, string options)
        {
            return action switch
            {
                "view_log" => $"tail {options} \"{fullPath}\"", // e.g., tail -n 20 log.txt
                "service_status" => $"systemctl status {Path.GetFileNameWithoutExtension(fullPath)} || ps aux | grep {Path.GetFileNameWithoutExtension(fullPath)}", // Service or process
                "run_script" => $"bash \"{fullPath}\" {options}", // bash script.sh args
                "create_file" => $"touch \"{fullPath}\" || echo \"{EscapeContent(content)}\" > \"{fullPath}\"", // touch or echo >
                "edit_file" => $"echo \"{EscapeContent(content)}\" >> \"{fullPath}\" || sed -i 's/.*/{EscapeContent(content)}/' \"{fullPath}\"", // Append or replace
                "delete_file" => $"rm \"{fullPath}\"", // rm file (but path restricted)
                "grep" => $"grep {options} \"{EscapeContent(pattern)}\" \"{fullPath}\"", // grep -i "error" log.txt
                "find" => $"find \"{Path.GetDirectoryName(fullPath) ?? _baseDir}\" {options} -name \"{EscapeContent(pattern)}\"", // find /dir -type f -name "*.log"
                _ => string.Empty
            };
        }

        private string EscapeContent(string content)
        {
            // Basic escaping for shell
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
