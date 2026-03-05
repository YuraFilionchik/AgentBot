#!/bin/bash

# Останавливаем скрипт при любой ошибке
set -e

# Запоминаем текущую рабочую директорию, где лежит backup_bot.sh
INITIAL_PWD=$(pwd)
# Или, если скрипт запускается из разных мест, определяем путь к нему точно:
# SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Константы
REPO_URL="https://github.com/YuraFilionchik/AgentBot"
TEMP_DIR="/tmp/agentbot_build"
PUBLISH_DIR="/home/dtdev/AgentBot"
SERVICE_NAME="agentbot.service"

# Вызываем бэкап через переменную пути (чтобы не зависеть от cd)
"$INITIAL_PWD/backup_bot.sh" make

echo "🚀 Начинаем процесс обновления $SERVICE_NAME..."

# 1. Очистка старой временной папки
if [ -d "$TEMP_DIR" ]; then
    echo "🧹 Очистка временной папки..."
    rm -rf "$TEMP_DIR"
fi

# 2. Клонирование репозитория
echo "📥 Клонирование репозитория..."
git clone "$REPO_URL" "$TEMP_DIR"

# 3. Переход в папку проекта и сборка
cd "$TEMP_DIR"

echo "🛠 Сборка и публикация проекта (.NET 9)..."
dotnet publish -c Release -r linux-x64 --self-contained false -o "$PUBLISH_DIR"

# Возвращаемся в исходную папку или используем полный путь
echo "🔄 Восстановление настроек из бэкапа..."
"$INITIAL_PWD/backup_bot.sh" restore

# 4. Настройка прав (рекомендуется раскомментировать и проверить пользователя)
# sudo chown -R dtdev:dtdev "$PUBLISH_DIR"

# 5. Перезапуск сервиса
echo "🔄 Перезапуск сервиса $SERVICE_NAME..."
sudo systemctl restart "$SERVICE_NAME"

# 6. Проверка статуса
echo "✅ Готово! Статус сервиса:"
sudo systemctl status "$SERVICE_NAME" --no-pager -n 5

# 7. Очистка
echo "🧹 Удаление временных файлов сборки..."
rm -rf "$TEMP_DIR"