using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgentBot.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentBot.Tools
{
    /// <summary>
    /// Tool for reading the bot's own logs: the application log file (Serilog)
    /// and the systemd service logs (journalctl). Lets the AI debug itself.
    /// </summary>
    public class BotLogsTool : IToolFunction
    {
        public string Name => "BotLogs";

        public string Description =>
            "Read the bot's own logs for diagnostics. " +
            "Actions: 'app' (read the application log file, last N lines), " +
            "'grep' (search the application log for a text pattern), " +
            "'service' (read systemd service logs via journalctl). " +
            "Parameters: lines (how many lines to return, default 50, max 500), " +
            "pattern (text to search for in 'grep'), " +
            "since (optional time filter for 'service', e.g. '10 minutes ago' or 'today'). " +
            "Use this when the user asks about errors, crashes, or why the bot did something.";

        public Dictionary<string, string> Parameters => new()
        {
            { "action", "string: app | grep | service" },
            { "lines", "number: how many lines to return (default 50, max 500)" },
            { "pattern", "string: search pattern (required for grep)" },
            { "since", "string: time filter for service logs (e.g. '10 minutes ago')" }
        };

        private readonly ILogger<BotLogsTool> _logger;
        private readonly IConfiguration _configuration;
        private readonly AccessControlService _accessControl;
        private readonly string _serviceName;

        public BotLogsTool(
            ILogger<BotLogsTool> logger,
            IConfiguration configuration,
            AccessControlService accessControl)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _accessControl = accessControl ?? throw new ArgumentNullException(nameof(accessControl));
            _serviceName = configuration["Service:Name"] ?? "agentbot";
        }

        public async Task<string> ExecuteAsync(Dictionary<string, object> args, long chatId = default)
        {
            if (!_accessControl.IsAdmin(chatId))
                return JsonSerializer.Serialize(new { error = "Access denied: admin privileges required." });

            string action = GetStringArg(args, "action").ToLowerInvariant();
            int lines = GetIntArg(args, "lines", 50);
            if (lines <= 0) lines = 50;
            if (lines > 500) lines = 500;
            string pattern = GetStringArg(args, "pattern");
            string since = GetStringArg(args, "since");

            _logger.LogInformation("BotLogs: action={Action}, lines={Lines} от chatId={ChatId}", action, lines, chatId);

            return action switch
            {
                "app" => await ReadAppLogAsync(lines),
                "grep" => await GrepAppLogAsync(pattern, lines),
                "service" => await ReadServiceLogAsync(lines, since),
                _ => JsonSerializer.Serialize(new { error = $"Unknown action '{action}'. Supported: app, grep, service." })
            };
        }

        // ────────────────────────────────────────────────
        //  App log (pure C#, no shell)
        // ────────────────────────────────────────────────

        private async Task<string> ReadAppLogAsync(int lines)
        {
            string? path = ResolveLogFile();
            if (path == null)
                return JsonSerializer.Serialize(new { error = "Не найден файл лога приложения." });

            try
            {
                var tail = await Task.Run(() => TailFile(path, lines));
                string output = string.Join(Environment.NewLine, tail);
                return JsonSerializer.Serialize(new { success = true, source = "app_log", path = path, lines = tail.Count, output = output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BotLogs: ошибка чтения лога {Path}", path);
                return JsonSerializer.Serialize(new { error = "Ошибка чтения лога: " + ex.Message });
            }
        }

        private async Task<string> GrepAppLogAsync(string pattern, int lines)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return JsonSerializer.Serialize(new { error = "Parameter 'pattern' is required for 'grep'." });

            string? path = ResolveLogFile();
            if (path == null)
                return JsonSerializer.Serialize(new { error = "Не найден файл лога приложения." });

            try
            {
                var matches = await Task.Run(() => GrepFile(path, pattern, lines));
                string output = string.Join(Environment.NewLine, matches);
                return JsonSerializer.Serialize(new { success = true, source = "app_log", path = path, pattern = pattern, matches = matches.Count, output = output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BotLogs: ошибка поиска в логе {Path}", path);
                return JsonSerializer.Serialize(new { error = "Ошибка поиска в логе: " + ex.Message });
            }
        }

        private static List<string> TailFile(string path, int lines)
        {
            var buffer = new LinkedList<string>();
            using var reader = new StreamReader(path);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                buffer.AddLast(line);
                if (buffer.Count > lines)
                    buffer.RemoveFirst();
            }
            return buffer.ToList();
        }

        private static List<string> GrepFile(string path, string pattern, int lines)
        {
            var buffer = new LinkedList<string>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    buffer.AddLast(line);
                    if (buffer.Count > lines)
                        buffer.RemoveFirst();
                }
            }
            return buffer.ToList();
        }

        // ────────────────────────────────────────────────
        //  Service log (journalctl)
        // ────────────────────────────────────────────────

        private async Task<string> ReadServiceLogAsync(int lines, string since)
        {
            string sinceArg = string.IsNullOrWhiteSpace(since)
                ? ""
                : $"--since \"{since.Replace("\"", "\\\"")}\" ";
            string unit = _serviceName.Replace("\"", "\\\"");

            var output = await RunBashAsync($"journalctl -u \"{unit}\" {sinceArg}-n {lines} --no-pager 2>&1");
            return JsonSerializer.Serialize(new { success = true, source = "service_log", unit = _serviceName, output = output });
        }

        private async Task<string> RunBashAsync(string command)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);

            using var process = new Process { StartInfo = psi };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = (await outputTask).Trim();
            string error = (await errorTask).Trim();
            return process.ExitCode != 0
                ? $"error (exit {process.ExitCode}): {error}".Trim()
                : output;
        }

        // ────────────────────────────────────────────────
        //  Log file resolution
        // ────────────────────────────────────────────────

        private string? ResolveLogFile()
        {
            // 1) Путь из конфигурации Serilog (реальный путь приложения)
            var configured = _configuration["Serilog:WriteTo:0:Args:path"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var candidate = Path.IsPathRooted(configured)
                    ? configured
                    : Path.Combine(Environment.CurrentDirectory, configured);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            // 2) Fallback: самый свежий *.log / *.txt в типичных директориях
            var found = new List<string>();
            foreach (var dir in new[] { "logs", "Logs", "log", "." })
            {
                var fullDir = Path.Combine(Environment.CurrentDirectory, dir);
                if (!Directory.Exists(fullDir)) continue;
                found.AddRange(Directory.GetFiles(fullDir, "*.log"));
                found.AddRange(Directory.GetFiles(fullDir, "*.txt"));
            }

            return found
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();
        }

        // ────────────────────────────────────────────────
        //  Argument helpers
        // ────────────────────────────────────────────────

        private static string GetStringArg(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var value) || value == null)
                return string.Empty;

            return value switch
            {
                string s => s,
                JsonElement je => je.ValueKind == JsonValueKind.String
                    ? (je.GetString() ?? string.Empty)
                    : (je.ToString() ?? string.Empty),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static int GetIntArg(Dictionary<string, object> args, string key, int defaultValue)
        {
            if (!args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            return value switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                JsonElement je when je.ValueKind == JsonValueKind.Number => je.TryGetInt32(out var n) ? n : defaultValue,
                JsonElement je when je.ValueKind == JsonValueKind.String => int.TryParse(je.GetString(), out var n) ? n : defaultValue,
                string s => int.TryParse(s, out var n) ? n : defaultValue,
                _ => defaultValue
            };
        }
    }
}
