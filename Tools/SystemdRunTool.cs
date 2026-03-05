using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using AgentBot.Security;
using Microsoft.Extensions.Logging;

namespace AgentBot.Tools
{
    /// <summary>
    /// Tool for managing one-off delayed tasks using systemd-run.
    /// Actions: create, list, stop.
    /// </summary>
    public class SystemdRunTool : IToolFunction
    {
        public string Name => "SystemdRun";

        public string Description =>
            "Manage one-off delayed tasks using systemd-run on Linux. " +
            "Actions: create (schedules a task), list (shows all timers), stop (cancels a task). " +
            "For 'create': provide delay (e.g., '15min', '2h', '45'), command (the command to run), and optionally unit (unique name) and description. " +
            "For 'stop': provide unit name. " +
            "For 'list': no extra parameters needed. " +
            "Use use_sudo=true for system-wide tasks (requires admin privileges).";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string: create | list | stop" },
            { "delay", "string: delay before execution (e.g., '15min', '2h', '30s', '45')" },
            { "command", "string: command to execute" },
            { "unit", "string: optional unique unit name" },
            { "description", "string: optional description for the task" },
            { "use_sudo", "boolean: execute with sudo (admin only)" }
        };

        private readonly ILogger<SystemdRunTool> _logger;
        private readonly AccessControlService _accessControl;

        public SystemdRunTool(ILogger<SystemdRunTool> logger, AccessControlService accessControl)
        {
            _logger = logger;
            _accessControl = accessControl;
        }

        public async Task<string> ExecuteAsync(Dictionary<string, object> args, long chatId = default)
        {
            string action = GetStringArg(args, "action").ToLowerInvariant();
            bool useSudo = GetBoolArg(args, "use_sudo");

            if (useSudo && !_accessControl.IsAdmin(chatId))
            {
                return JsonSerializer.Serialize(new { error = "Sudo access denied: admin privileges required." });
            }

            return action switch
            {
                "create" => await HandleCreateAsync(args, useSudo),
                "list" => await HandleListAsync(useSudo),
                "stop" => await HandleStopAsync(args, useSudo),
                _ => JsonSerializer.Serialize(new { error = $"Unknown action '{action}'. Supported: create, list, stop." })
            };
        }

        private async Task<string> HandleCreateAsync(Dictionary<string, object> args, bool useSudo)
        {
            string delay = GetStringArg(args, "delay");
            string command = GetStringArg(args, "command");
            string unit = GetStringArg(args, "unit");
            string description = GetStringArg(args, "description");

            if (string.IsNullOrWhiteSpace(delay) || string.IsNullOrWhiteSpace(command))
            {
                return JsonSerializer.Serialize(new { error = "Parameters 'delay' and 'command' are required for 'create'." });
            }

            string sudo = useSudo ? "sudo " : "";
            string unitArg = !string.IsNullOrWhiteSpace(unit) ? $"--unit=\"{unit}\" " : "";
            string descArg = !string.IsNullOrWhiteSpace(description) ? $"--description=\"{description}\" " : "";

            // Default to --user if not sudo, but systemd-run --user might fail in some environments
            // If useSudo is false, we might want to still use sudo if the bot runs as a user that can sudo without password
            // or just run as the current user. The prompt example showed systemd-run without --user.
            string userArg = useSudo ? "" : "--user ";

            string fullCommand = $"{sudo}systemd-run {userArg}--on-active={delay} {unitArg}{descArg}{command}";

            try
            {
                var output = await RunBashCommandAsync(fullCommand);
                return JsonSerializer.Serialize(new { success = true, output = output, command = fullCommand });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to create task: {ex.Message}", command = fullCommand });
            }
        }

        private async Task<string> HandleListAsync(bool useSudo)
        {
            string sudo = useSudo ? "sudo " : "";
            string userArg = useSudo ? "" : "--user ";
            string command = $"{sudo}systemctl {userArg}list-timers --all";

            try
            {
                var output = await RunBashCommandAsync(command);
                return JsonSerializer.Serialize(new { success = true, output = output });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to list timers: {ex.Message}" });
            }
        }

        private async Task<string> HandleStopAsync(Dictionary<string, object> args, bool useSudo)
        {
            string unit = GetStringArg(args, "unit");
            if (string.IsNullOrWhiteSpace(unit))
            {
                return JsonSerializer.Serialize(new { error = "Parameter 'unit' is required for 'stop'." });
            }

            string sudo = useSudo ? "sudo " : "";
            string userArg = useSudo ? "" : "--user ";

            // Определяем имена юнитов
            string baseUnit = unit;
            if (unit.EndsWith(".timer")) baseUnit = unit[..^6];
            else if (unit.EndsWith(".service")) baseUnit = unit[..^8];

            string timerUnit = $"{baseUnit}.timer";
            string serviceUnit = $"{baseUnit}.service";

            string stopTimer = $"{sudo}systemctl {userArg}stop {timerUnit}";
            string stopService = $"{sudo}systemctl {userArg}stop {serviceUnit}";

            try
            {
                // Пытаемся остановить таймер и сервис.
                // Не бросаем исключение, если одно из них не удалось (например, если таймер уже сработал и исчез)
                string timerResult = "";
                try { timerResult = await RunBashCommandAsync(stopTimer); } catch (Exception ex) { timerResult = $"Timer stop failed: {ex.Message}"; }

                string serviceResult = "";
                try { serviceResult = await RunBashCommandAsync(stopService); } catch (Exception ex) { serviceResult = $"Service stop failed: {ex.Message}"; }

                return JsonSerializer.Serialize(new { success = true, timer_output = timerResult, service_output = serviceResult });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to stop task: {ex.Message}" });
            }
        }

        private async Task<string> RunBashCommandAsync(string command)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception(string.IsNullOrWhiteSpace(error) ? $"Exit code {process.ExitCode}" : error.Trim());
            }

            return output.Trim();
        }

        private static string GetStringArg(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var value)) return string.Empty;
            return value?.ToString() ?? string.Empty;
        }

        private static bool GetBoolArg(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var value)) return false;
            if (value is bool b) return b;
            if (value is JsonElement je) return je.ValueKind == JsonValueKind.True;
            return false;
        }
    }
}
