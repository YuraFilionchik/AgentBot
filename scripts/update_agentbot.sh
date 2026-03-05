#!/bin/bash

set -e

INITIAL_PWD=$(pwd)

# Константы
REPO_URL="https://github.com/YuraFilionchik/AgentBot"
# Меняем /tmp на постоянную папку для хранения исходников
SOURCE_CODE_DIR="/home/dtdev/agentbot_source" 
PUBLISH_DIR="/home/dtdev/AgentBot"
SERVICE_NAME="agentbot.service"

# 1. Делаем бэкап текущих конфигов перед любыми действиями
"$INITIAL_PWD/backup_bot.sh" make

echo "🚀 Начинаем процесс обновления $SERVICE_NAME..."

# 2. Получение исходного кода (Pull или Clone)
if [ -d "$SOURCE_CODE_DIR/.git" ]; then
    echo "git  Обновление существующего репозитория (git pull)..."
    cd "$SOURCE_CODE_DIR"
    # Отменяем локальные изменения, если они вдруг появились, и тянем новое
    git reset --hard
    git pull
else
    echo "📥 Папка не найдена. Клонирование репозитория..."
    mkdir -p "$SOURCE_CODE_DIR"
    git clone "$REPO_URL" "$SOURCE_CODE_DIR"
    cd "$SOURCE_CODE_DIR"
fi

# 3. Сборка
echo "🛠 Сборка и публикация проекта (.NET 9)..."
# Добавил --nologo для более чистого вывода
dotnet publish -c Release -r linux-x64 --self-contained false -o "$PUBLISH_DIR" --nologo

# 4. Восстановление настроек
# Важно: делаем это ДО рестарта, чтобы сервис подхватил свежие файлы
echo "🔄 Восстановление настроек из бэкапа..."
"$INITIAL_PWD/backup_bot.sh" restore

echo "🔄 Восстановление прав доступа к скриптам...  "
chown root:root $INITIAL_PWD/backup_bot.sh
chown root:root $INITIAL_PWD/restart_agentbot.sh
chown root:root $INITIAL_PWD/update_agentbot.sh
chmod +x $INITIAL_PWD/backup_bot.sh
chmod +x $INITIAL_PWD/restart_agentbot.sh
chmod +x $INITIAL_PWD/update_agentbot.sh

# 5. Перезапуск сервиса
echo "🔄 Запланирован перезапуск сервиса $SERVICE_NAME..."

# Запускаем рестарт в фоне с задержкой в 2 секунды, 
# чтобы этот скрипт успел отпустить управление и не был убит мгновенно
(sleep 2 && sudo systemctl restart "$SERVICE_NAME") & 

echo "✅ Скрипт обновления передал команду системе и завершается."
exit 0

# 6. Проверка результата
echo "✅ Обновление завершено успешно!"
sudo systemctl status "$SERVICE_NAME" --no-pager -n 5