# AgentBot — Многофункциональный Telegram-бот с ИИ-агентом и персональной базой знаний

## Обзор проекта

**AgentBot** — это фоновый сервис (демон) на .NET 9 для работы с Telegram-ботами и ИИ-агентами. Приложение обрабатывает входящие сообщения, маршрутизируя команды через фиксированные обработчики, а обычные сообщения — через ИИ-агента с поддержкой инструментов (tools).

**Основное назначение:**
- Polling сообщений из Telegram
- Обработка команд: `/start`, `/help`, `/alias`, `/cron`, `/weather`, `/note` и др.
- **Персональная база знаний (алиасы)** — пользовательские сокращения для команд и терминов
- **Cron-задачи** — планирование задач по расписанию с выполнением через ИИ
- **Отправка файлов** — возможность отправки документов пользователю
- **Telegram-клавиатуры** — ReplyKeyboardMarkup и InlineKeyboardMarkup для быстрого доступа
- ИИ-обработка обычных сообщений через Google Gemini с функциями
- Выполнение инструментов: погода, заметки, Linux-команды (с поддержкой sudo)
- Работа как systemd-сервис на Linux (24/7)

---

## Стек технологий

| Компонент | Технология |
|-----------|------------|
| **Фреймворк** | .NET 9 (Worker Service) |
| **Хостинг** | Microsoft.Extensions.Hosting + Systemd |
| **DI** | Microsoft.Extensions.DependencyInjection |
| **Логирование** | Serilog.AspNetCore |
| **БД** | SQLite (Microsoft.Data.Sqlite) |
| **Telegram Bot** | Telegram.Bot v22.9.0 |
| **ИИ-агент** | Google.GenAI v1.2.0 |
| **Конфигурация** | appsettings.json + env vars |
| **Контейнеризация** | Docker |

---

## Структура проекта

```
AgentBot/
├── Program.cs                 # Точка входа, регистрация DI
├── BotWorker.cs               # BackgroundService для polling
├── AgentBot.csproj            # Проект .NET 9
├── appsettings.json           # Конфигурация
├── Dockerfile                 # Docker-образ
│
├── Interfaces/
│   ├── IBotProvider.cs        # Интерфейс бота
│   ├── IAiAgent.cs            # Интерфейс ИИ-агента
│   ├── IToolFunction.cs       # Интерфейс инструмента
│   └── IConversationMemory.cs # Интерфейс памяти разговоров
│
├── Bots/
│   ├── TelegramBotProvider.cs # Реализация IBotProvider
│   └── TelegramFileSender.cs  # Отправка файлов
│
├── AiAgents/
│   ├── GeminiAiAgent.cs       # Реализация IAiAgent (Gemini)
│   ├── GrokAgent.cs           # Заготовка для Grok
│   └── OpenAiAgent.cs         # Заготовка для OpenAI
│
├── Handlers/
│   ├── CommandHandler.cs      # Обработка /команд
│   └── MessageProcessor.cs    # Маршрутизация сообщений
│
├── Memory/
│   ├── IConversationMemory.cs
│   ├── InMemoryConversationStorage.cs
│   └── SQLiteConversationStorage.cs
│
├── Models/
│   ├── Alias.cs               # Модель алиаса
│   ├── CronTask.cs            # Модель Cron-задачи
│   ├── LlmContext.cs          # Контекст для LLM
│   ├── QuickCommand.cs        # Быстрая команда
│   └── InlineButton.cs        # Inline-кнопки Telegram
│
├── Services/
│   ├── IAliasService.cs       # Сервис алиасов
│   ├── SQLiteAliasService.cs
│   ├── ICronTaskService.cs    # Сервис Cron-задач
│   ├── SQLiteCronTaskService.cs
│   ├── CronTaskRunner.cs      # Фоновый сервис для Cron
│   ├── IKeyboardService.cs    # Сервис клавиатур
│   ├── SQLiteKeyboardService.cs
│   ├── ILlmWrapper.cs         # Обёртка для LLM-запросов
│   └── LlmWrapper.cs
│
└── Tools/
    ├── LinuxCMDTool.cs        # Выполнение Linux-команд (с sudo)
    ├── WeatherTool.cs         # Получение погоды
    ├── SendMessageTool.cs     # Отправка сообщений пользователю
    ├── SendFileTool.cs        # Отправка файлов пользователю
    └── DatabaseTool.cs        # Заготовка
```

---

## Архитектура

### Поток обработки сообщений

```
1. BotWorker (BackgroundService)
   └── Запускает TelegramBotProvider.StartPollingAsync()

2. TelegramBotProvider
   └── Получает Update → вызывает MessageProcessor.ProcessAsync()

3. MessageProcessor
   ├── Если текст начинается с "/" → CommandHandler.HandleCommandAsync()
   └── Иначе → IAiAgent.ProcessMessageAsync() с tools

4. GeminiAiAgent
   ├── Загружает контекст пользователя через ILlmWrapper
   ├── Формирует системный промпт с алиасами и знаниями
   ├── Отправляет запрос в Google Gemini с function declarations
   ├── При function call → находит IToolFunction, выполняет ExecuteAsync()
   └── Возвращает ответ через BotProvider.SendMessageAsync()

5. CronTaskRunner (фоновый сервис)
   └── Каждую минуту проверяет задачи и выполняет через IAiAgent
```

### Интерфейсы

**IBotProvider:**
```csharp
Task StartPollingAsync(CancellationToken cancellationToken = default);
Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
Task SendFileAsync(long chatId, byte[] fileContent, string fileName, string? caption = null, CancellationToken cancellationToken = default);
Task SendFileFromPathAsync(long chatId, string filePath, string? caption = null, CancellationToken cancellationToken = default);
Func<long, string, Task>? OnMessageReceived { get; set; }
```

**IAiAgent:**
```csharp
Task<string> ProcessMessageAsync(long chatId, string message, List<IToolFunction> tools);
```

**IToolFunction:**
```csharp
string Name { get; }
string Description { get; }
Dictionary<string, string> Parameters { get; }
Task<string> ExecuteAsync(Dictionary<string, object> args);
```

**IConversationMemory:**
```csharp
Task AddMessageAsync(long chatId, Content message);
Task<List<Content>> GetHistoryAsync(long chatId, int maxMessages);
Task ClearAsync(long chatId);
Task CleanupAsync();
```

**IAliasService:**
```csharp
Task<Alias> AddAliasAsync(long userId, string aliasName, string value, AliasType type);
Task<bool> DeleteAliasAsync(long userId, string aliasName);
Task<List<Alias>> GetAllAliasesAsync(long userId);
Task<string> GetAliasesContextAsync(long userId);
Task<string?> ResolveCommandAliasAsync(string text);
```

**ICronTaskService:**
```csharp
Task<CronTask> CreateTaskAsync(long userId, string name, string description, string cronExpression);
Task<bool> DeleteTaskAsync(long userId, long taskId);
Task<List<CronTask>> GetAllTasksAsync(long userId);
Task<List<CronTask>> GetDueTasksAsync();
Task MarkTaskAsRunAsync(long taskId);
```

---

## Сборка и запуск

### Локальная разработка (Windows)

```bash
# Восстановление зависимостей
dotnet restore

# Сборка
dotnet build

# Запуск в режиме разработки
dotnet run

# Публикация
dotnet publish -c Release -r linux-x64 --self-contained true
```

### Docker

```bash
# Сборка образа
docker build -t agentbot .

# Запуск контейнера
docker run -d --name agentbot \
  -e Bots__Telegram__ApiToken=YOUR_TOKEN \
  -e AiAgent__ApiKey=YOUR_GEMINI_KEY \
  agentbot
```

### Развертывание на Linux (systemd)

1. **Публикация:**
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained true -o /opt/agentbot
   ```

2. **Создание systemd-юнита** (`/etc/systemd/system/agentbot.service`):
   ```ini
   [Unit]
   Description=AgentBot Telegram Bot
   After=network.target

   [Service]
   Type=notify
   User=nonroot
   WorkingDirectory=/opt/agentbot
   ExecStart=/opt/agentbot/AgentBot
   Restart=always
   Environment="DOTNET_ENVIRONMENT=Production"

   [Install]
   WantedBy=multi-user.target
   ```

3. **Управление сервисом:**
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable agentbot
   sudo systemctl start agentbot
   sudo systemctl status agentbot
   ```

---

## Конфигурация

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": { "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}" }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/app.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7
        }
      }
    ]
  },
  "Bots": {
    "Telegram": {
      "ApiToken": "YOUR_TELEGRAM_BOT_TOKEN"
    }
  },
  "AiAgent": {
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "Model": "gemini-1.5-pro"
  },
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
    "AllowedActions": "view_log,service_status,run_script,edit_file,create_file,delete_file,grep,find",
    "SudoAllowedActions": "service_status,run_script"
  },
  "WeatherApiKey": "YOUR_OPENWEATHER_API_KEY",
  "AppBaseDir": "/app"
}
```

### Переменные окружения

| Переменная | Описание |
|------------|----------|
| `Bots__Telegram__ApiToken` | Токен Telegram-бота |
| `AiAgent__ApiKey` | API-ключ Google Gemini |
| `AiAgent__Model` | Модель Gemini |
| `WeatherApiKey` | API-ключ OpenWeatherMap |
| `AppBaseDir` | Базовая директория приложения |
| `Memory__DatabasePath` | Путь к файлу SQLite БД для истории |
| `Alias__DatabasePath` | Путь к БД алиасов |
| `Cron__DatabasePath` | Путь к БД Cron-задач |

---

## Инструменты (Tools)

### LinuxCMDTool
Выполняет Linux-команды с расширенными возможностями:
- **Действия:** `view_log`, `service_status`, `run_script`, `create_file`, `edit_file`, `delete_file`, `grep`, `find`
- **Безопасность:** whitelist команд, санитизация ввода
- **Sudo:** поддержка выполнения от имени суперпользователя (настраивается)
- **Пути:** работа как в базовой директории, так и с произвольными путями (для администраторов)

**Параметры:**
- `action` — тип операции
- `path` — путь к файлу/директории
- `pattern` — шаблон для grep/find
- `content` — содержимое для записи
- `options` — дополнительные опции
- `use_sudo` — выполнить через sudo
- `allow_any_path` — разрешить работу вне базовой директории

### WeatherTool
Получает погоду через OpenWeatherMap API:
- **Параметры:** `city` (название города)
- **Возвращает:** температура, описание, город

### SendMessageTool
Отправляет текстовое сообщение пользователю в Telegram с поддержкой inline-кнопок:
- **Параметры:**
  - `chat_id` (number) — Telegram Chat ID
  - `text` (string) — текст сообщения
  - `parse_mode` (string, опционально) — "Markdown" или "HTML"
  - `inline_buttons` (array, опционально) — массив inline-кнопок для интерактивности
- **Возвращает:** `{ success: true, message: "Сообщение отправлено", chat_id: ..., buttons_count: N }`

**Пример с inline-кнопками:**
```json
{
  "chat_id": 123456789,
  "text": "Выберите действие:",
  "inline_buttons": [
    [
      {"label": "✅ Да", "callback_data": "confirm_yes"},
      {"label": "❌ Нет", "callback_data": "confirm_no"}
    ],
    [
      {"label": "📊 Статистика", "callback_data": "show_stats"}
    ]
  ]
}
```

**Формат inline_buttons:**
- Массив строк (каждая строка — отдельный ряд кнопок)
- Каждая кнопка: `{"label": "Текст", "callback_data": "data"}` или `{"label": "Текст", "url": "https://..."}`
- `callback_data` передаётся боту при нажатии (до 64 байт)
- `url` открывает ссылку в браузере (опционально)

### SendFileTool
Отправляет файл пользователю в Telegram:
- **Параметры:**
  - `chat_id` (number) — Telegram Chat ID
  - `file_name` (string) — имя файла
  - `content` (string) — содержимое файла (если создаётся на лету)
  - `file_path` (string) — путь к файлу (если отправляется существующий)
  - `caption` (string, опционально) — подпись к файлу
- **Возвращает:** `{ success: true, message: "Файл отправлен", chat_id: ..., file_name: ... }`

---

## Персональная база знаний (алиасы)

Система алиасов позволяет пользователям создавать персональные сокращения:

### Типы алиасов

1. **Command** — алиас команды
   ```
   /alias погода /weather
   /alias заметки /note
   ```

2. **Knowledge** — алиас знания для ИИ
   ```
   /alias cymmes это приложение blazortool knowledge
   /alias K8S Kubernetes knowledge
   ```

### Как это работает

1. Пользователь создаёт алиас через `/alias`
2. Алиас сохраняется в SQLite (`aliases.db`)
3. При обработке сообщения ИИ-агент загружает алиасы через `ILlmWrapper`
4. Формируется системный промпт с контекстом пользователя
5. Если первое слово сообщения совпадает с алиасом команды — оно заменяется

**Пример:**
```
Пользователь: погода Москва
ИИ понимает: /weather Москва
```

---

## Cron-задачи

Планировщик задач для автоматического выполнения действий по расписанию.

### Создание задачи

```
/cron morning "0 8 * * *" Отправить доброе утро
/cron check_logs "*/30 * * * *" Проверить логи на ошибки
```

### Формат cron-выражения

```
минута час день месяц день_недели
```

| Поле | Диапазон |
|------|----------|
| Минута | 0-59 |
| Час | 0-23 |
| День | 1-31 |
| Месяц | 1-12 |
| День недели | 0-6 (0 = воскресенье) |

### Примеры

| Выражение | Описание |
|-----------|----------|
| `0 10 * * *` | Каждый день в 10:00 |
| `*/5 * * * *` | Каждые 5 минут |
| `0 9 * * 1` | Каждый понедельник в 9:00 |
| `0 0 1 * *` | 1-го числа каждого месяца |
| `30 14 * * 1-5` | В будни в 14:30 |

### Выполнение задач

1. `CronTaskRunner` (фоновый сервис) проверяет задачи каждую минуту
2. Задачи с `NextRun <= now` выполняются через ИИ-агент
3. Результат отправляется пользователю в чат
4. Задача отмечается как выполненная, вычисляется следующее время

---

## Telegram-клавиатуры

### ReplyKeyboardMarkup (основная клавиатура)

Автоматически генерируется с кнопками:
- 📋 Помощь (`/help`)
- 📝 Заметки (`/note`)
- 🌤 Погода (`/weather`)
- 📚 Алиасы (`/listaliases`)
- ⏰ Задачи (`/listcrons`)
- ℹ️ О боте (`/about`)

### InlineKeyboardMarkup (inline-кнопки)

Используется для контекстных действий:
- 📋 Алиасы → `aliases_list`
- ⏰ Задачи → `crons_list`
- ➕ Добавить алиас → `alias_add`
- ➕ Добавить задачу → `cron_add`
- ❌ Закрыть → `close`

---

## Логирование

Serilog настроен на запись в консоль и файл с ротацией по дням.

**Примеры логов:**
- `BotWorker started.` — запуск сервиса
- `Telegram polling started.` — начало polling
- `Команда /alias от chatId=123456` — команда
- `Chat 123456: Gemini ← сообщение` — запрос к ИИ
- `Chat 123456: Gemini вызвал функцию: LinuxCMD` — вызов инструмента
- `Выполнение задачи #1: morning` — выполнение Cron-задачи

---

## Безопасность

### Контроль доступа (RBAC)

**AccessControlService** — система разграничения прав доступа на основе ролей.

#### Регистрация администратора
```
/register <пароль>
```
- Пароль задаётся в `appsettings.json` → `Security:AdminPassword`
- После успешной регистрации chatId сохраняется в `admins.json`
- Подтверждение роли: `/whoami` → показывает "👑 Администратор"

#### Права доступа

| Действие | Пользователь | Администратор |
|----------|--------------|---------------|
| Выполнение команд в разрешённых директориях | ✅ | ✅ |
| Выполнение действий из `AllowedActions` | ✅ | ✅ |
| Выполнение команд с `sudo` | ❌ | ✅ (если в `SudoAllowedActions`) |
| Доступ к произвольным путям (`allow_any_path=true`) | ❌ | ✅ |

#### Проверка прав в LinuxCMDTool

```csharp
// Проверка sudo - ТОЛЬКО для администраторов
if (useSudo)
{
    if (!_accessControl.IsAdmin(chatId))
        return "Sudo access denied: admin privileges required.";
}

// Проверка пути - для не-администраторов только разрешённые директории
if (allowAnyPath)
{
    if (!_accessControl.IsAdmin(chatId))
        return "Arbitrary path access denied: admin privileges required.";
}
```

#### Конфигурация

```json
"Security": {
  "AdminPassword": "ChangeMeInProduction123!",
  "InitialAdminIds": []
}
```

#### Файлы безопасности

| Файл | Описание |
|------|----------|
| `admins.json` | Список chatId администраторов (создаётся автоматически) |

---

### LinuxCMDTool

- **Whitelist действий** — только разрешённые команды из `AllowedActions`
- **Санитизация ввода** — удаление опасных символов (`;`, `&`, `|`, `$`, `` ` ``)
- **Запрещённые команды** — `rm -rf /`, `mkfs`, `dd` и другие деструктивные операции
- **Sudo** — только для разрешённых действий, настраивается через `AllowSudo`
- **Пути** — ограничение на базовую директорию (опционально расширяется для администраторов)

---

### Docker

- Изоляция процесса
- Запуск от непривилегированного пользователя

---

## Известные ограничения

- `GrokAgent.cs`, `OpenAiAgent.cs` — заготовки
- Ограничение на размер истории (по умолчанию 20 сообщений)
- Cron-задачи выполняются в UTC времени

---

## Полезные команды

```bash
# Проверка кода
dotnet build

# Запуск тестов
dotnet test

# Просмотр логов systemd
journalctl -u agentbot -f

# Перезапуск сервиса
sudo systemctl restart agentbot
```

---

## Файлы данных

| Файл | Описание |
|------|----------|
| `conversations.db` | История разговоров |
| `aliases.db` | Алиасы пользователей |
| `cron.db` | Cron-задачи |
| `keyboard.db` | Быстрые команды |
| `admins.json` | Список администраторов (chatId) |
| `logs/app.log` | Логи приложения |
