<p align="center">
  <img src=".github/assets/banner.png" alt="AgentBot Banner" width="100%" />
</p>

<h1 align="center">🤖 AgentBot</h1>

<p align="center">
  <b>AI-Powered Telegram Bot with Tool Execution, Personal Knowledge Base & Scheduled Tasks</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 9" />
  <img src="https://img.shields.io/badge/Telegram-Bot_API-26A5E4?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" />
  <img src="https://img.shields.io/badge/Google-Gemini-8E75B2?style=for-the-badge&logo=googlegemini&logoColor=white" alt="Gemini AI" />
  <img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite" />
  <img src="https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white" alt="Docker" />
  <img src="https://img.shields.io/badge/Linux-systemd-FCC624?style=for-the-badge&logo=linux&logoColor=black" alt="Systemd" />
</p>

<p align="center">
  <a href="#-features">Features</a> •
  <a href="#%EF%B8%8F-architecture">Architecture</a> •
  <a href="#-tech-stack">Tech Stack</a> •
  <a href="#-getting-started">Getting Started</a> •
  <a href="#-deployment">Deployment</a> •
  <a href="#-commands">Commands</a> •
  <a href="#-security">Security</a>
</p>

---

## 📌 Overview

**AgentBot** is a self-hosted, AI-powered Telegram bot built with **.NET 9** that serves as a personal assistant capable of executing Linux commands, managing notes, scheduling tasks, and answering natural language queries — all through a Telegram chat interface.

Unlike simple chatbots, AgentBot features a **function-calling AI agent** (powered by Google Gemini) that can autonomously invoke system tools, query weather APIs, manage files, and interact with the underlying Linux server — making it a true **AI agent**, not just a chat wrapper.

### 💡 What Makes It Special

- 🧠 **AI Agent with Tool Use** — The bot doesn't just chat; it *acts*. It can execute shell commands, send files, and query APIs by deciding which tools to call.
- 📚 **Personal Knowledge Base** — Users create aliases that inject personal context into every AI interaction.
- ⏰ **Cron-like Task Scheduler** — Schedule recurring tasks described in natural language. The AI agent executes them and reports back.
- 🔐 **Role-Based Access Control** — Granular permissions with admin registration, sudo control, and path restrictions.
- 🔌 **Multi-Provider AI** — Swap between Gemini, OpenAI, and Grok agents via a single config change.

---

## ✨ Features

<table>
  <tr>
    <td width="50%">

### 🤖 AI Agent & Natural Language
- Google Gemini integration with function calling
- Pluggable AI providers (Gemini / OpenAI / Grok)
- Conversation memory with per-chat isolation
- Context-aware responses using personal knowledge base
- Processes text, photos, voice messages & documents

</td>
<td width="50%">

### 🛠️ Tool Execution
- **Linux CMD** — Run shell commands with sandboxed security
- **Weather** — Real-time weather via OpenWeatherMap API
- **Notes** — CRUD operations on personal notes (SQLite)
- **File Sender** — Create and send files to users on-the-fly
- **Systemd Manager** — Manage Linux services remotely
- **Bot Management** — Runtime bot control and restarts

</td>
  </tr>
  <tr>
    <td>

### 📚 Personal Knowledge Base (Aliases)
- **Command aliases** — shortcuts like `weather` → `/weather`
- **Knowledge aliases** — teach the AI custom terms and context
- All aliases persist in SQLite and auto-inject into AI prompts
- Per-user isolation — each user has their own knowledge base

</td>
<td>

### ⏰ Scheduled Tasks (Cron)
- Natural language task descriptions executed by AI
- Standard cron expressions (5-field format)
- Background runner checks every minute
- AI-driven execution with results sent to chat
- Full CRUD: create, list, delete tasks

</td>
  </tr>
</table>

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Telegram Cloud                         │
└─────────────────────────┬───────────────────────────────────┘
                          │ Polling
┌─────────────────────────▼───────────────────────────────────┐
│  BotWorker (BackgroundService)                              │
│  └── TelegramBotProvider                                    │
│       └── MessageProcessor                                  │
│            ├── /command  →  CommandHandler                   │
│            │                 ├── /alias, /cron, /weather...  │
│            │                 └── AccessControlService        │
│            │                                                │
│            └── text msg  →  AI Agent (Gemini/OpenAI/Grok)   │
│                              ├── LlmWrapper (context build) │
│                              ├── ConversationMemory          │
│                              └── Tool Execution Loop         │
│                                   ├── LinuxCMDTool          │
│                                   ├── WeatherTool           │
│                                   ├── SendMessageTool       │
│                                   ├── SendFileTool          │
│                                   ├── CronTool              │
│                                   ├── NotesTools (CRUD)     │
│                                   ├── BotManagementTool     │
│                                   └── SystemdRunTool        │
├─────────────────────────────────────────────────────────────┤
│  CronTaskRunner (BackgroundService)                         │
│  └── Checks due tasks every minute → Executes via AI Agent  │
├─────────────────────────────────────────────────────────────┤
│  Storage Layer (SQLite)                                     │
│  ├── conversations.db  — Chat history                       │
│  ├── aliases.db        — Knowledge base & command aliases   │
│  ├── cron.db           — Scheduled tasks                    │
│  └── keyboard.db       — Custom keyboard layouts            │
└─────────────────────────────────────────────────────────────┘
```

### Message Processing Flow

```
User Message → TelegramBotProvider → MessageProcessor
                                         │
                    ┌────────────────────┤
                    │                    │
              starts with "/"      plain text
                    │                    │
                    ▼                    ▼
             CommandHandler        GeminiAiAgent
             (direct action)       ├── Build context (aliases + history)
                                   ├── Send to Gemini with tool declarations
                                   ├── If function_call → execute tool
                                   │   └── Return result → continue loop
                                   └── Send final response to user
```

---

## 🧰 Tech Stack

| Layer | Technology | Purpose |
|-------|-----------|---------|
| **Runtime** | .NET 9 (Worker Service) | Long-running background service |
| **Hosting** | Microsoft.Extensions.Hosting + Systemd | Process lifecycle & systemd integration |
| **Telegram** | Telegram.Bot v22.9.0 | Bot API client with polling |
| **AI Engine** | Google.GenAI v1.2.0 | Gemini models with function calling |
| **Database** | SQLite (Microsoft.Data.Sqlite) | Lightweight embedded storage |
| **Logging** | Serilog | Structured logging with file rotation |
| **Resilience** | Polly v8.6.5 | Retry policies for API calls |
| **DI** | Microsoft.Extensions.DependencyInjection | Built-in IoC container |
| **Containerization** | Docker + Docker Compose | Isolated deployment |
| **CI/CD** | GitHub Actions | Automated build & publish |

---

## 📁 Project Structure

```
AgentBot/
├── Program.cs                    # Entry point & DI registration
├── BotWorker.cs                  # BackgroundService — starts polling
├── AgentBot.csproj               # .NET 9 Worker project
├── appsettings.json              # Configuration
├── Dockerfile                    # Multi-stage Docker build
├── docker-compose.yml            # Container orchestration
│
├── Interfaces/
│   ├── IBotProvider.cs           # Bot abstraction (send/receive)
│   ├── IAiAgent.cs               # AI agent contract
│   ├── IToolFunction.cs          # Tool interface (name, params, execute)
│   └── IConversationMemory.cs    # Chat history storage contract
│
├── AiAgents/
│   ├── GeminiAiAgent.cs          # Google Gemini with function calling
│   ├── GrokAgent.cs              # xAI Grok integration
│   └── OpenAiAgent.cs            # OpenAI integration
│
├── Bots/
│   ├── TelegramBotProvider.cs    # Telegram polling & message dispatch
│   └── TelegramFileSender.cs     # File upload to Telegram
│
├── Handlers/
│   ├── CommandHandler.cs         # Slash command router
│   └── MessageProcessor.cs       # Message routing (command vs AI)
│
├── Tools/
│   ├── LinuxCMDTool.cs           # Shell execution (sandboxed, sudo)
│   ├── WeatherTool.cs            # OpenWeatherMap API
│   ├── SendMessageTool.cs        # Send messages with inline buttons
│   ├── SendFileTool.cs           # Create & send files
│   ├── CronTool.cs               # Cron task management
│   ├── BotManagementTool.cs      # Bot runtime control
│   ├── SystemdRunTool.cs         # Deferred task execution
│   └── DatabaseTool.cs           # Notes CRUD operations
│
├── Services/
│   ├── IAliasService.cs          # Alias management contract
│   ├── SQLiteAliasService.cs     # SQLite alias storage
│   ├── ICronTaskService.cs       # Cron task contract
│   ├── SQLiteCronTaskService.cs  # SQLite cron storage
│   ├── CronTaskRunner.cs         # Background cron executor
│   ├── IKeyboardService.cs       # Telegram keyboard management
│   ├── SQLiteKeyboardService.cs  # SQLite keyboard storage
│   └── LlmWrapper.cs            # Context builder for AI prompts
│
├── Memory/
│   ├── InMemoryConversationStorage.cs  # In-memory chat history
│   └── SQLiteConversationStorage.cs    # Persistent chat history
│
├── Models/
│   ├── Alias.cs                  # Alias entity
│   ├── CronTask.cs               # Cron task entity
│   ├── LlmContext.cs             # AI prompt context
│   ├── QuickCommand.cs           # Keyboard shortcut
│   └── InlineButton.cs           # Telegram inline button
│
├── Security/
│   └── AccessControlService.cs   # RBAC with admin registration
│
└── scripts/
    ├── backup_bot.sh             # Automated backup script
    ├── restart_agentbot.sh       # Service restart helper
    └── update_agentbot.sh        # Update & redeploy script
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A [Telegram Bot Token](https://t.me/BotFather) (free)
- A [Google Gemini API Key](https://aistudio.google.com/app/apikey) (free tier available)
- *(Optional)* [OpenWeatherMap API Key](https://openweathermap.org/api) for weather functionality

### 1. Clone the Repository

```bash
git clone https://github.com/YuraFilionchik/AgentBot.git
cd AgentBot
```

### 2. Configure

Create `Config/appsettings.json` with your secrets:

```json
{
  "Bots": {
    "Telegram": {
      "ApiToken": "YOUR_TELEGRAM_BOT_TOKEN"
    }
  },
  "AiAgent": {
    "Provider": "gemini",
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "Model": "gemini-2.0-flash"
  },
  "WeatherApiKey": "YOUR_OPENWEATHER_API_KEY",
  "Security": {
    "AdminPassword": "YourSecurePassword123!"
  }
}
```

### 3. Build & Run

```bash
dotnet restore
dotnet build
dotnet run
```

The bot will start polling Telegram and respond to your messages immediately.

---

## 🐳 Deployment

### Docker

```bash
# Build the image
docker build -t agentbot .

# Run with environment variables
docker run -d --name agentbot \
  -e Bots__Telegram__ApiToken=YOUR_TOKEN \
  -e AiAgent__ApiKey=YOUR_GEMINI_KEY \
  -e AiAgent__Provider=gemini \
  agentbot
```

### Docker Compose

```bash
# Configure .env file with your tokens, then:
docker compose up -d
```

### Linux (systemd)

```bash
# 1. Publish self-contained binary
dotnet publish -c Release -r linux-x64 --self-contained true -o /opt/agentbot

# 2. Create systemd service
sudo tee /etc/systemd/system/agentbot.service << EOF
[Unit]
Description=AgentBot Telegram Bot
After=network.target

[Service]
Type=notify
User=agentbot
WorkingDirectory=/opt/agentbot
ExecStart=/opt/agentbot/AgentBot
Restart=always
Environment="DOTNET_ENVIRONMENT=Production"

[Install]
WantedBy=multi-user.target
EOF

# 3. Enable & start
sudo systemctl daemon-reload
sudo systemctl enable --now agentbot
```

### CI/CD

The project includes a **GitHub Actions** workflow (`.github/workflows/dotnet.yml`) that automatically:
- Restores dependencies
- Publishes a self-contained single-file binary for Linux x64
- Uploads the build artifact as a downloadable zip

---

## 📋 Commands

| Command | Description | Access |
|---------|-------------|--------|
| `/start` | Welcome message & keyboard | 👤 All |
| `/help` | Full command reference | 👤 All |
| `/about` | Bot info & capabilities | 👤 All |
| `/status` | Uptime, version, loaded tools | 👤 All |
| `/whoami` | Your chat ID, username & role | 👤 All |
| `/weather <city>` | Current weather | 👤 All |
| `/note <text>` | Save a personal note | 👤 All |
| `/alias <name> <value> [type]` | Create command or knowledge alias | 👤 All |
| `/listaliases` | List all your aliases | 👤 All |
| `/deletealias <name>` | Remove an alias | 👤 All |
| `/cron <name> "<expr>" <desc>` | Schedule a recurring AI task | 👤 All |
| `/listcrons` | List your scheduled tasks | 👤 All |
| `/deletecron <id>` | Remove a scheduled task | 👤 All |
| `/register <password>` | Register as administrator | 👤 All |
| `/restart` | Restart the bot service | 👑 Admin |
| `/run <delay> <command>` | Execute a deferred task | 👑 Admin |
| `/timers` | List active systemd timers | 👑 Admin |

> **Any message not starting with `/`** is routed to the AI agent for natural language processing with full tool access.

---

## 🔐 Security

### Role-Based Access Control

AgentBot implements a **two-tier permission system**:

| Capability | 👤 User | 👑 Admin |
|-----------|---------|---------|
| Chat with AI agent | ✅ | ✅ |
| Use notes, aliases, weather | ✅ | ✅ |
| Execute commands in allowed directories | ✅ | ✅ |
| Execute commands with `sudo` | ❌ | ✅ |
| Access arbitrary system paths | ❌ | ✅ |
| Restart bot / manage services | ❌ | ✅ |

### Admin Registration

```
/register <admin_password>
/whoami  →  👑 Administrator
```

### LinuxCMDTool Safety

- ✅ **Action whitelist** — Only explicitly allowed operations
- ✅ **Input sanitization** — Strips dangerous characters (`;`, `&`, `|`, `$`, `` ` ``)
- ✅ **Blocked commands** — `rm -rf /`, `mkfs`, `dd` and other destructive operations
- ✅ **Directory sandboxing** — Users restricted to allowed directories
- ✅ **Sudo gating** — Requires admin role + action in `SudoAllowedActions`

---

## ⚙️ Configuration Reference

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `Bots__Telegram__ApiToken` | Telegram Bot API token | ✅ |
| `AiAgent__ApiKey` | AI provider API key | ✅ |
| `AiAgent__Provider` | `gemini`, `openai`, or `grok` | ❌ (default: `gemini`) |
| `AiAgent__Model` | Model name (e.g., `gemini-2.0-flash`) | ❌ |
| `WeatherApiKey` | OpenWeatherMap API key | ❌ |
| `Security__AdminPassword` | Password for `/register` | ❌ |
| `Memory__MaxMessagesPerChat` | Chat history limit | ❌ (default: `20`) |

### API Keys (Free Tiers Available)

| Service | Get Your Key | Free? |
|---------|-------------|-------|
| Telegram Bot | [@BotFather](https://t.me/BotFather) | ✅ Unlimited |
| Google Gemini | [AI Studio](https://aistudio.google.com/app/apikey) | ✅ Free tier |
| OpenWeatherMap | [openweathermap.org](https://openweathermap.org/api) | ✅ Limited |

---

## 📊 Data Storage

All data is stored locally in SQLite databases — **no external database required**.

| File | Content | Format |
|------|---------|--------|
| `conversations.db` | Chat history per user | SQLite |
| `aliases.db` | Personal aliases & knowledge | SQLite |
| `cron.db` | Scheduled tasks | SQLite |
| `keyboard.db` | Custom keyboard layouts | SQLite |
| `admins.json` | Registered admin chat IDs | JSON |
| `logs/app.log` | Application logs (daily rotation) | Text |

---

## 🗂️ Key Design Decisions

- **Worker Service pattern** — The app runs as a long-lived daemon, not a web server, which is ideal for a polling-based bot.
- **Interface-driven design** — `IBotProvider`, `IAiAgent`, `IToolFunction` allow easy extension and testing.
- **Factory-based AI provider selection** — Switch AI backends with a single config change, no code modification needed.
- **Tool loop architecture** — The AI agent can chain multiple tool calls in a single conversation turn, enabling complex multi-step operations.
- **Embedded SQLite** — Zero external dependencies for data storage; everything runs on a single machine.
- **Systemd-native** — Built-in `AddSystemd()` support for proper Linux daemon integration with watchdog notifications.

---

## 🛣️ Roadmap

- [ ] Unit & integration test suite
- [ ] Rate limiting for abuse prevention
- [ ] Suspicious activity audit logging
- [ ] Web dashboard for monitoring
- [ ] Voice message transcription
- [ ] Image analysis with multimodal AI

---

## 📄 License

This project is open source. Feel free to use it for learning, personal projects, and portfolio demonstrations.

---

<p align="center">
  <b>Built with ❤️ using .NET 9, Google Gemini & Telegram Bot API</b>
</p>
