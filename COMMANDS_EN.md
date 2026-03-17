# 📖 AgentBot Command Manual

> **Version:** 2.0
> **Last updated:** 2026-03-01
> **Platform:** Telegram

---

## 🔑 Access Levels

| Icon | Role | Description |
|------|------|-------------|
| 👤 | **User** | Default access for everyone |
| 👑 | **Administrator** | Extended privileges: sudo, arbitrary paths |

### Administrator Registration

**Command:** `/register <password>`

**Usage:**
```
/register ChangeMeInProduction123!
```

**What registration provides:**
- ✅ Execute commands with `sudo` (for actions from `SudoAllowedActions`)
- ✅ Access to arbitrary system paths (`allow_any_path=true`)
- ✅ Manage services and processes

**Role verification:**
```
/whoami
```
Shows: "👑 Administrator" or "👤 User"

---

## 📋 Complete Command List

### 🔹 Basic Commands

---

#### `/start`
Starts the bot and displays a welcome message.

**Usage:**
```
/start
```

---

#### `/help`
Displays help for all available commands.

**Usage:**
```
/help
```

---

#### `/about`
Information about the bot: technologies, features, purpose.

**Usage:**
```
/about
```

---

#### `/status`
Checks the current bot status.

**Usage:**
```
/status
```

**What it shows:**
- Status (online/offline)
- Current server time (UTC)
- Uptime since last start
- Bot version
- Number of loaded tools

---

#### `/whoami`
Displays information about your Telegram account and role.

**Usage:**
```
/whoami
```

**What it shows:**
- `Chat ID` — unique ID of your chat
- Username
- First and last name
- **Role** — 👑 Administrator or 👤 User

---

### 🔐 Security

#### `/register <password>`
Registers you as an administrator to gain extended privileges.

**Usage:**
```
/register ChangeMeInProduction123!
```

**What it provides:**
- ✅ Execute commands with `sudo`
- ✅ Access to arbitrary system paths
- ✅ Manage services and processes

**Responses:**
- `✅ You have been successfully registered as an administrator!` — success
- `⚠️ You are already an administrator.` — already registered
- `❌ Invalid administrator password.` — wrong password

---

#### `/restart`
Restarts the bot. Available only to administrators.

**Usage:**
```
/restart
```

**Requirements:**
- 👑 Administrator role (obtain via `/register`)

**What it does:**
1. Attempts to execute `systemctl restart agentbot` (Linux systemd)
2. If systemctl is unavailable — terminates the process (Docker/systemd will restart)

**Response:**
```
🔄 Restarting bot...

The bot will be restarted within a few seconds.
If the bot does not start automatically — check the logs.
```

---

### 📚 Aliases (Knowledge Base)

Aliases allow creating personal shortcuts for commands and terms.

---

#### `/alias <name> <value> [type]`
Creates a new alias.

**Usage:**
```
/alias weather /weather
/alias notes /note
/alias cymmes this is blazortool application knowledge
```

**Arguments:**

| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `name` | string | ✅ Yes | Alias name |
| `value` | string | ✅ Yes | Command or description |
| `type` | string | ⬜ No | `command` (default) or `knowledge` |

**Alias types:**
- **command** — command alias (e.g., "weather" → "/weather")
- **knowledge** — knowledge alias for AI (e.g., "cymmes" — "blazortool application")

**Example response:**
```
🔹 Alias created:
"weather" → "/weather"
```

---

#### `/deletealias <name>`
Deletes an alias by name.

**Usage:**
```
/deletealias weather
```

---

#### `/listaliases`
Displays all your aliases.

**Usage:**
```
/listaliases
```

**Example response:**
```
📚 Your aliases:

🔹 Commands:
  weather → /weather
  notes → /note

📖 Knowledge:
  cymmes — this is blazortool application
```

---

### ⏰ Cron Tasks

Schedule tasks by cron expression with execution via AI agent.

---

#### `/cron <name> <cron-expression> <description>`
Creates a new Cron task.

**Usage:**
```
/cron morning "0 8 * * *" Send good morning message
/cron check_logs "*/30 * * * *" Check logs for errors
```

**Arguments:**

| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `name` | string | ✅ Yes | Short task name |
| `cron-expression` | string | ✅ Yes | Schedule in cron format |
| `description` | string | ✅ Yes | Text task for AI |

**Cron expression format (5 fields):**
```
minute hour day month day_of_week
```

**Examples:**
| Expression | Description |
|------------|-------------|
| `0 10 * * *` | Every day at 10:00 |
| `*/5 * * * *` | Every 5 minutes |
| `0 9 * * 1` | Every Monday at 9:00 |
| `0 0 1 * *` | 1st of every month at 00:00 |
| `30 14 * * 1-5` | Weekdays at 14:30 |

**Example response:**
```
⏰ Task created:
📌 Name: morning
⏱ Schedule: 0 8 * * *
📝 Description: Send good morning message
📅 Next execution: 2026-03-02 08:00:00 UTC
```

---

#### `/listcrons`
Displays all your Cron tasks.

**Usage:**
```
/listcrons
```

**Example response:**
```
⏰ Your tasks:

🟢 #1 morning
   Schedule: 0 8 * * *
   Description: Send good morning message
   Next: 2026-03-02 08:00:00 UTC

🟢 #2 check_logs
   Schedule: */30 * * * *
   Description: Check logs for errors
   Next: 2026-03-01 14:30:00 UTC
```

---

#### `/deletecron <ID>`
Deletes a Cron task by ID.

**Usage:**
```
/deletecron 1
```

---

### ⏱ Deferred Tasks (systemd-run)

Manage one-time tasks that should execute after a certain time.

#### `/run <delay> ["description"] <command>`
Runs a task after the specified time.

**Usage:**
```
/run 15min "Photo backup" /home/user/backup.sh
/run 2h "Temp cleanup" rm -rf /tmp/*
/run 45s "Test" echo "Hello"
```

**Arguments:**
- `delay` — wait time (e.g., `15min`, `2h`, `45s`, `30`).
- `description` — (optional) task description in quotes.
- `command` — Linux command to execute.

---

#### `/timers`
Displays list of all active and past timers (systemd timers).

**Usage:**
```
/timers
```

---

#### `/stoprun <unit>`
Stops a deferred task by unit name.

**Usage:**
```
/stoprun run-u123
```

---

### 🌤 Weather

---

#### `/weather <city>`
Gets current weather in the specified city.

**Usage:**
```
/weather Moscow
/weather Minsk
/weather New York
```

**Example response:**
```
🌤 Weather in Moscow:
🌡 Temperature: -3.2°C
📋 Conditions: light snow
```

> ⚙️ **Requires configuration:** `WeatherApiKey` in `appsettings.json`

---

### 📝 Notes

---

#### `/note <text>`
Saves a note to the local SQLite database.

**Usage:**
```
/note Buy milk
/note Call Pete tomorrow at 3 PM
```

**Example response:**
```
📝 Note #7 saved!
"Buy milk"
```

---

## 🤖 Working with AI Agent

Any message **not starting with `/`** is sent directly to the AI agent (Gemini).

**Examples:**
```
How to install nginx on Ubuntu?
Write an SQL query to select top-10 users
Explain the difference between TCP and UDP
```

### Personal Context

The AI agent uses your personal context:
- **Command aliases** — if you write "weather Moscow", AI understands it as "/weather Moscow"
- **Knowledge aliases** — if you created alias "cymmes — blazortool application", AI will use this knowledge in responses
- **Chat history** — AI remembers conversation context (up to 20 last messages)

---

## 📩 Other Message Types

| Type | Bot Behavior |
|------|--------------|
| 📸 Photo | Accepts, reports receipt |
| 🎙 Voice | Accepts, reports receipt |
| 📄 Document | Accepts, shows file name |
| Text | → AI agent or command |

---

## ⚙️ Configuration (`appsettings.json`)

```json
{
  "Bots": {
    "Telegram": {
      "ApiToken": "YOUR_TELEGRAM_BOT_TOKEN"
    }
  },
  "AiAgent": {
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "Model": "gemini-1.5-pro"
  },
  "WeatherApiKey": "YOUR_OPENWEATHERMAP_KEY",
  "AppBaseDir": "/app",
  "Memory": {
    "MaxMessagesPerChat": "20",
    "DatabasePath": "conversations.db"
  },
  "Alias": {
    "DatabasePath": "aliases.db"
  },
  "Cron": {
    "DatabasePath": "cron.db"
  },
  "Keyboard": {
    "DatabasePath": "keyboard.db"
  },
  "LinuxCMD": {
    "AllowSudo": false,
    "AllowedDirs": "logs,scripts,data",
    "AllowedActions": "view_log,service_status,run_script",
    "SudoAllowedActions": "service_status,run_script"
  }
}
```

### Obtaining Tokens

| Key | Where to Get | Free |
|-----|--------------|------|
| `Bots:Telegram:ApiToken` | [@BotFather](https://t.me/BotFather) | ✅ |
| `AiAgent:ApiKey` (Gemini) | [aistudio.google.com](https://aistudio.google.com/app/apikey) | ✅ |
| `WeatherApiKey` | [openweathermap.org/api](https://openweathermap.org/api) | ✅ (limited) |

---

## 🗂️ Data Storage

| Data | File | Format |
|------|------|--------|
| Conversation history | `conversations.db` | SQLite |
| Aliases | `aliases.db` | SQLite |
| Cron tasks | `cron.db` | SQLite |
| Quick commands | `keyboard.db` | SQLite |
| Administrators | `admins.json` | JSON |
| Logs | `logs/app.log` | Text (daily rotation) |

---

## ❓ FAQ

**Q: Bot doesn't respond to my messages**
A: Check that `Bots:Telegram:ApiToken` is filled. Check the logs.

**Q: `/weather` says "tool not configured"**
A: Fill in `WeatherApiKey` in `appsettings.json`.

**Q: How does the alias system work?**
A: Aliases are stored in SQLite and automatically inserted into AI agent context. If you created alias "weather" → "/weather", then receiving message "weather Moscow" will be converted to "/weather Moscow".

**Q: How to create a scheduled task?**
A: Use command `/cron <name> "<cron>" <description>`. The task will execute automatically, and the result will be sent to your chat.

**Q: How to become an administrator?**
A: Use command `/register <password>`. Password is set in `appsettings.json` → `Security:AdminPassword`. After registration, you will gain extended privileges: executing commands with `sudo` and access to arbitrary paths.

**Q: How to check my role?**
A: Use command `/whoami` — it will show "👑 Administrator" or "👤 User".

**Q: How does AI know about my permissions?**
A: AI agent sees your chatId and can check permissions via `/whoami` command. To execute commands with `sudo` or access system paths (/etc, /var/log), AI must pass `use_sudo=true` or `allow_any_path=true` parameters to LinuxCMD tool.

**Q: Can I use sudo in Linux commands?**
A: Yes, but only for administrators. Configure `LinuxCMD:AllowSudo: true` and specify actions in `SudoAllowedActions`. Regular users cannot execute commands with `sudo`.

**Q: Why doesn't AI use allow_any_path?**
A: AI agent should check your role via `/whoami` before using `allow_any_path` or `use_sudo`. If you are an administrator — explicitly ask AI: "execute with administrator privileges" or "use allow_any_path".

**Q: How to reset conversation history?**
A: Delete `conversations.db` file and restart the bot.
