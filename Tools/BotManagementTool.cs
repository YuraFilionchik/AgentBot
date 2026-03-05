using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using AgentBot.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Tools
{
    /// <summary>
    /// Tool for AI agent to manage the bot: restart, update, backup, restore.
    /// All actions require admin privileges.
    /// Scripts are expected at scripts/ relative to the working directory.
    /// </summary>
    public class BotManagementTool : IToolFunction
    {
        public string Name => "BotManager";

        public string Description =>
            "Manage the bot service. Admin-only actions: " +
            "restart (restart the bot service), " +
            "update (pull latest code, rebuild, restart — includes automatic backup), " +
            "backup (create a backup of the bot), " +
            "restore (restore from latest or specified backup), " +
            "backup_list (list available backups). " +
            "For 'restore' you can optionally provide 'backup_file' (full path to .tar.gz). " +
            "All actions require admin privileges.";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string: restart | update | backup | restore | backup_list" },
            { "backup_file", "string: optional path to backup file (for restore)" }
        };

        private readonly ILogger<BotManagementTool> _logger;
        private readonly AccessControlService _accessControl;
        private readonly string _scriptsDir;

        public BotManagementTool(
            ILogger<BotManagementTool> logger,
            IConfiguration configuration,
            AccessControlService accessControl)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _accessControl = accessControl ?? throw new ArgumentNullException(nameof(accessControl));
            _scriptsDir = configuration["BotManagement:ScriptsDir"] ?? "scripts";
        }

        public async Task<string> ExecuteAsync(Dictionary<string, object> args, long chatId = default)
        {
            string action = GetStringArg(args, "action").ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action))
                return JsonSerializer.Serialize(new { error = "Missing 'action' parameter. Supported: restart, update, backup, restore, backup_list." });

            if (!_accessControl.IsAdmin(chatId))
                return JsonSerializer.Serialize(new { error = "Access denied: admin privileges required." });

            _logger.LogInformation("BotManager: action={Action}, chatId={ChatId}", action, chatId);

            return action switch
            {
                "restart" => await HandleRestartAsync(),
                "update" => await HandleUpdateAsync(),
                "backup" => await HandleBackupAsync(),
                "restore" => await HandleRestoreAsync(args),
                "backup_list" => await HandleBackupListAsync(),
                _ => JsonSerializer.Serialize(new { error = $"Unknown action '{action}'. Supported: restart, update, backup, restore, backup_list." })
            };
        }

        private async Task<string> HandleRestartAsync()
        {
            _logger.LogInformation("BotManager: restarting service");
            return await RunScriptAsync($"{_scriptsDir}/restart_agentbot.sh");
        }

        private async Task<string> HandleUpdateAsync()
        {
            _logger.LogInformation("BotManager: starting update");
            // update_agentbot.sh uses INITIAL_PWD=$(pwd) to locate backup_bot.sh,
            // so we must cd into the scripts directory before running it.
            // Using systemd-run ensures the update process is in a separate transient unit.
            return await RunScriptAsync($"cd {_scriptsDir} && sudo systemd-run --collect bash update_agentbot.sh");
        }

        private async Task<string> HandleBackupAsync()
        {
            _logger.LogInformation("BotManager: creating backup");
            return await RunScriptAsync($"{_scriptsDir}/backup_bot.sh make");
        }

        private async Task<string> HandleRestoreAsync(Dictionary<string, object> args)
        {
            string backupFile = GetStringArg(args, "backup_file");
            _logger.LogInformation("BotManager: restoring from {BackupFile}", 
                string.IsNullOrEmpty(backupFile) ? "latest" : backupFile);

            var scriptPath = $"{_scriptsDir}/backup_bot.sh";
            string scriptArgs = string.IsNullOrWhiteSpace(backupFile)
                ? $"{scriptPath} restore"
                : $"{scriptPath} restore {backupFile}";

            return await RunScriptAsync(scriptArgs);
        }

        private async Task<string> HandleBackupListAsync()
        {
            _logger.LogInformation("BotManager: listing backups");
            // backup_bot.sh has no 'list' case — list backups directly
            return await RunScriptAsync("ls -lht /home/dtdev/backups/AgentBot/agentbot_backup_*.tar.gz 2>/dev/null || echo 'No backups found.'");
        }

        private async Task<string> RunScriptAsync(string command, string? stdinText = null)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = stdinText is not null,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                processInfo.ArgumentList.Add("-c");
                processInfo.ArgumentList.Add(command);

                using var process = new Process { StartInfo = processInfo };
                process.Start();

                if (stdinText is not null)
                {
                    await process.StandardInput.WriteLineAsync(stdinText);
                    process.StandardInput.Close();
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0)
                {
                    _logger.LogError("BotManager: script failed (exit {ExitCode}): {Error}", process.ExitCode, error);
                    return JsonSerializer.Serialize(new { success = false, error = $"Script failed (exit {process.ExitCode}): {error.Trim()}" });
                }

                _logger.LogInformation("BotManager: script completed: {Output}", output.Trim());
                return JsonSerializer.Serialize(new { success = true, output = output.Trim() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BotManager: script execution error");
                return JsonSerializer.Serialize(new { success = false, error = $"Execution failed: {ex.Message}" });
            }
        }

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
    }
}
