# Multi-functional Extensible .NET Application for Bots and AI

Information about frameworks and libraries verified for actuality via official sources (NuGet, Microsoft Docs, Google AI Docs). Current versions: .NET 9, Telegram.Bot 22.9.0 (Bot API 9.4 support), Google.GenAI 1.1.0 (GA since May 2025).

## 1. Application Purpose
The application is designed to create a background service (daemon) on Linux that:
- Works with bots (initially Telegram, extensible to others, e.g., Discord).
- Processes incoming messages: commands (starting with "/") — via fixed logic in code; regular messages — via AI agent (Gemini by default, extensible to other models/APIs).
- AI agent has access to a set of tools for task execution (e.g., getting weather, saving notes, executing safe Linux commands).
- Provides flexible configuration via JSON file, action logging to file, systemd integration for Linux.
- Goal: Extensible chatbot with AI, suitable for automation tasks, assistants, monitoring (via tools like LinuxCMDTool).

The application runs as a long-lived 24/7 process, with polling for messages, graceful shutdown, and security (tool restrictions, input sanitization).

## 2. Overall Architecture
- **Project Type:** Worker Service (.NET Console App with hosting, `dotnet new worker` template).
- **Principles:** Modular design with interfaces for extensibility (bots, AI, tools). Dependency Injection (DI) via Microsoft.Extensions.DependencyInjection. Asynchronous processing (async/await). Polling cycle for bot, message routing (commands → handlers, text → AI with tools).
- **Workflow:**
  1. Startup: Config reading, service registration in DI, BotWorker launch (BackgroundService) for polling.
  2. Message reception from bot (TelegramBotProvider).
  3. Processing in MessageProcessor: if "/", → CommandHandler; otherwise → GeminiAiAgent with tools (function calling cycle).
  4. Tool execution (if called by AI), response return via bot.
  5. Logging all actions (Serilog to file).
- **Extensibility:** New bots/AI/tools — new interface implementations + config/DI update. Dynamic loading (assembly) for plugins.
- **Security:** Input sanitization in tools (regex for dangerous characters), action/directory whitelists, banned commands, non-root execution.

## 3. Technology Stack
- **Language/Framework:** C# on .NET 9
- **Hosting/Service:** Microsoft.Extensions.Hosting (Worker Service with systemd integration via Microsoft.Extensions.Hosting.Systemd v10.0.x).
- **Configuration:** Microsoft.Extensions.Configuration (appsettings.json + env vars).
- **DI:** Microsoft.Extensions.DependencyInjection.
- **Logging:** Serilog.AspNetCore (file rotation, levels from config).
- **Bot (Telegram):** Telegram.Bot v22.9.0 (current as of March 2026, Bot API 9.4 support; NuGet.org) + Telegram.Bot.Extensions.Polling v1.0.x.
- **AI Agent (Gemini):** Google.GenAI v1.1.0 (current as of March 2026, GA since May 2025; NuGet.org/Google Docs).
- **HTTP/External APIs:** System.Net.Http (HttpClientFactory).
- **Database (for tools):** Microsoft.Data.Sqlite (local SQLite for simple tasks, like SaveNoteTool).
- **Other:** System.Text.Json (serialization), System.Diagnostics.Process (for shell in LinuxCMDTool).
- **Build/Deployment:** dotnet publish for linux-x64 self-contained. Systemd unit file for daemon.

## 4. Project Structure
- **Project Root:** TelegramAiBot.csproj (or AgentBot.csproj), Program.cs (host, DI), BotWorker.cs (BackgroundService for polling).
- **Directories and Key Files:**
  - **Bots/**: TelegramBotProvider.cs (IBotProvider implementation for polling/sending).
  - **AiAgents/**: GeminiAiAgent.cs (IAiAgent implementation with function calling).
  - **Tools/**: WeatherTool.cs, SaveNoteTool.cs, LinuxCMDTool.cs (IToolFunction implementations; LinuxCMDTool with grep, find, tail, systemctl, etc.).
  - **Handlers/**: CommandHandler.cs (command processing: /start, /help, etc.), MessageProcessor.cs (message routing).
  - **Interfaces/**: IBotProvider.cs, IAiAgent.cs, IToolFunction.cs.
  - **Config/**: appsettings.json (logs, bots, AI, tools).
  - **Logs/**: Directory for log files (app.log with rotation).
  - **Additional:** ToolConverter.cs (if needed for tool schemas), TelegramAiBot.service (systemd unit for Linux).

Total volume: ~15 files, modular for expansion.

## 5. Key Components
- **Interfaces:**
  - IBotProvider: StartPollingAsync(), SendMessageAsync(chatId, text), update handling.
  - IAiAgent: ProcessMessageAsync(message, tools) — AI request with tool calling cycle.
  - IToolFunction: Name, Description, Parameters, ExecuteAsync(args) — tool execution.
- **Implementations:**
  - TelegramBotProvider: Polling with HandleUpdateAsync, MessageProcessor injection.
  - GeminiAiAgent: Client (Google.GenAI), chat history (List<Content>), tool conversion to FunctionDeclaration, function call/response processing cycle.
  - Tools: WeatherTool (HttpClient for API), SaveNoteTool (SQLite), LinuxCMDTool (Process for bash, with action whitelists: view_log, grep, find, etc.).
  - CommandHandler: Command dictionary (/start, /help), logic + response sending.
  - MessageProcessor: "/" check, handler or AI invocation, response sending.
- **Configuration (appsettings.json example):**
  ```json
  {
    "Logging": { "LogFilePath": "logs/app.log", "LogLevel": "Information" },
    "Bots": { "ActiveBot": "Telegram", "Telegram": { "ApiToken": "..." } },
    "AiAgent": { "Provider": "Gemini", "ApiKey": "...", "Model": "gemini-1.5-pro" },
    "Tools": [ { "Name": "GetWeather", "Description": "..." } ]
  }
  ```
- **Logging:** Serilog to file/console, records: commands, AI actions, tool calls.

## 6. Extension and Support
- **New Bots:** IBotProvider implementation, add to config/DI.
- **New AI:** IAiAgent implementation (switch by provider in config).
- **New Tools:** IToolFunction implementation, register in DI (IEnumerable<IToolFunction> in AI).
- **Testing:** Unit tests (API mocks), integration tests (test bot).
- **Scalability:** Queues (RabbitMQ for background), Docker for isolation.

## 7. Linux Deployment
- **Build:** `dotnet publish -c Release -r linux-x64 --self-contained true`.
- **Run:** As systemd service (/etc/systemd/system/app.service: ExecStart=/path/to/app, Restart=always, User=nonroot).
- **Commands:** `systemctl start/enable/restart app`, logs in `journalctl -u app`.
