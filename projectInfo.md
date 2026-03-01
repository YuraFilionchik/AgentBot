# Отчет по многофункциональному расширяемому приложению на .NET для работы с ботами и ИИ

**Дата отчета:** 01 марта 2026 г.  
**Основание:** Анализ всей беседы, включая дизайн, структуру, кодовые примеры и обсуждения ошибок. Информация по фреймворкам и библиотекам проверена на актуальность через официальные источники (NuGet, Microsoft Docs, Google AI Docs). Актуальные версии: .NET 9, Telegram.Bot 22.9.0 (поддержка Bot API 9.4), Google.GenAI 1.1.0 (GA с мая 2025).

## 1. Назначение приложения
Приложение предназначено для создания фонового сервиса (демона) на Linux, который:
- Работает с ботами (начально Telegram, расширяемо на другие, например Discord).
- Обрабатывает входящие сообщения: команды (начинающиеся с "/") — фиксированной логикой в коде; обычные сообщения — ИИ-агентом (Gemini по умолчанию, расширяемо на другие модели/API).
- ИИ-агент имеет доступ к набору инструментов (tools) для выполнения задач (например, получение погоды, сохранение заметок, выполнение безопасных Linux-команд).
- Обеспечивает гибкую конфигурацию через JSON-файл, логирование действий в файл, интеграцию с systemd для Linux.
- Цель: Расширяемый чат-бот с ИИ, подходящий для задач автоматизации, помощников, мониторинга (через tools как LinuxCMDTool).

Приложение работает как долгоживущий процесс 24/7, с polling для сообщений, graceful shutdown и безопасностью (ограничения на tools, санитизация ввода).

## 2. Общая архитектура
- **Тип проекта:** Worker Service (.NET Console App с хостингом, шаблон `dotnet new worker`).
- **Принципы:** Модульный дизайн с интерфейсами для расширяемости (bots, AI, tools). Dependency Injection (DI) через Microsoft.Extensions.DependencyInjection. Асинхронная обработка (async/await). Цикл polling для бота, маршрутизация сообщений (команды → handlers, текст → AI с tools).
- **Поток работы:**
  1. Запуск: Чтение конфига, регистрация сервисов в DI, запуск BotWorker (BackgroundService) для polling.
  2. Получение сообщения от бота (TelegramBotProvider).
  3. Обработка в MessageProcessor: если "/", → CommandHandler; иначе → GeminiAiAgent с tools (цикл function calling).
  4. Выполнение tools (если вызваны ИИ), возврат ответа через бот.
  5. Логирование всех действий (Serilog в файл).
- **Расширяемость:** Новые боты/ИИ/tools — новые реализации интерфейсов + обновление конфига/DI. Динамическая загрузка (assembly) для плагинов.
- **Безопасность:** Санитизация ввода в tools (regex для опасных символов), whitelist действий/директорий, banned команды, non-root запуск.

## 3. Стек технологий
- **Язык/Фреймворк:** C# на .NET 9
- **Хостинг/Сервис:** Microsoft.Extensions.Hosting (Worker Service с systemd-интеграцией via Microsoft.Extensions.Hosting.Systemd v10.0.x).
- **Конфигурация:** Microsoft.Extensions.Configuration (appsettings.json + env vars).
- **DI:** Microsoft.Extensions.DependencyInjection.
- **Логирование:** Serilog.AspNetCore (ротация файлов, уровни из конфига).
- **Бот (Telegram):** Telegram.Bot v22.9.0 (актуально на март 2026, поддержка Bot API 9.4; NuGet.org) + Telegram.Bot.Extensions.Polling v1.0.x.
- **ИИ-агент (Gemini):** Google.GenAI v1.1.0 (актуально на март 2026, GA с мая 2025; NuGet.org/Google Docs).
- **HTTP/Внешние API:** System.Net.Http (HttpClientFactory).
- **БД (для tools):** Microsoft.Data.Sqlite (локальная SQLite для простых задач, как SaveNoteTool).
- **Другие:** System.Text.Json (сериализация), System.Diagnostics.Process (для shell в LinuxCMDTool).
- **Сборка/Развертывание:** dotnet publish для linux-x64 self-contained. Systemd unit-файл для демона.

## 4. Структура проекта
- **Корень проекта:** TelegramAiBot.csproj (или AgentBot.csproj), Program.cs (хост, DI), BotWorker.cs (BackgroundService для polling).
- **Директории и ключевые файлы:**
  - **Bots/**: TelegramBotProvider.cs (реализация IBotProvider для polling/отправки).
  - **AiAgents/**: GeminiAiAgent.cs (реализация IAiAgent с function calling).
  - **Tools/**: WeatherTool.cs, SaveNoteTool.cs, LinuxCMDTool.cs (реализации IToolFunction; LinuxCMDTool с grep, find, tail, systemctl и т.д.).
  - **Handlers/**: CommandHandler.cs (обработка /команд: /start, /help и т.д.), MessageProcessor.cs (маршрутизация сообщений).
  - **Interfaces/**: IBotProvider.cs, IAiAgent.cs, IToolFunction.cs.
  - **Config/**: appsettings.json (логи, боты, ИИ, tools).
  - **Logs/**: Директория для лог-файлов (app.log с ротацией).
  - **Дополнительно:** ToolConverter.cs (если нужен для схем tools), TelegramAiBot.service (systemd unit для Linux).

Общий объем: ~15 файлов, модульный для расширения.

## 5. Ключевые компоненты
- **Интерфейсы:**
  - IBotProvider: StartPollingAsync(), SendMessageAsync(chatId, text), обработка обновлений.
  - IAiAgent: ProcessMessageAsync(message, tools) — запрос к ИИ с циклом tool calling.
  - IToolFunction: Name, Description, Parameters, ExecuteAsync(args) — выполнение инструмента.
- **Реализации:**
  - TelegramBotProvider: Поллинг с HandleUpdateAsync, инъекция MessageProcessor.
  - GeminiAiAgent: Клиент Client (Google.GenAI), история чата (List<Content>), конвертация tools в FunctionDeclaration, цикл обработки function calls/responses.
  - Tools: WeatherTool (HttpClient для API), SaveNoteTool (SQLite), LinuxCMDTool (Process для bash, с whitelist действий: view_log, grep, find и т.д.).
  - CommandHandler: Словарь команд (/start, /help), логика + отправка ответа.
  - MessageProcessor: Проверка на "/", вызов handlers или ИИ, отправка ответа.
- **Конфигурация (appsettings.json пример):**
  ```json
  {
    "Logging": { "LogFilePath": "logs/app.log", "LogLevel": "Information" },
    "Bots": { "ActiveBot": "Telegram", "Telegram": { "ApiToken": "..." } },
    "AiAgent": { "Provider": "Gemini", "ApiKey": "...", "Model": "gemini-1.5-pro" },
    "Tools": [ { "Name": "GetWeather", "Description": "..." } ]
  }
  ```
- **Логирование:** Serilog в файл/консоль, записи: команды, действия ИИ, вызовы tools.

## 6. Расширение и поддержка
- **Новые боты:** Реализация IBotProvider, добавление в конфиг/DI.
- **Новые ИИ:** Реализация IAiAgent (switch по провайдеру в конфиге).
- **Новые tools:** Реализация IToolFunction, регистрация в DI (IEnumerable<IToolFunction> в ИИ).
- **Тестирование:** Unit-тесты (mocks для API), интеграционные (тестовый бот).
- **Масштабируемость:** Очереди (RabbitMQ для фона), Docker для изоляции.


## 7. Развертывание на Linux
- **Сборка:** `dotnet publish -c Release -r linux-x64 --self-contained true`.
- **Запуск:** Как systemd-сервис (/etc/systemd/system/app.service: ExecStart=/path/to/app, Restart=always, User=nonroot).
- **Команды:** `systemctl start/enable/restart app`, логи в `journalctl -u app`.