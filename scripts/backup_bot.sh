#!/bin/bash

SOURCE_DIR="/home/dtdev/AgentBot"
BACKUP_BASE_DIR="/home/dtdev/backups/AgentBot"
TIMESTAMP=$(date +"%Y-%m-%d_%H-%M-%S")
BACKUP_NAME="agentbot_backup_$TIMESTAMP"
BACKUP_PATH="$BACKUP_BASE_DIR/$BACKUP_NAME"

# Список исключений (что НЕ надо бэкапить)
# Можно указывать папки, конкретные файлы или маски (*.log)
EXCLUDES=(
    "logs/"
    "temp/"
    "*.tmp"
    ".pdb"
    "*.dll"
)

# --- ПРОВЕРКИ ---
if ! command -v rsync &> /dev/null; then
    echo "❌ Ошибка: rsync не установлен. Установите его: sudo apt install rsync"
    exit 1
fi

# Подготовка аргументов для rsync из массива исключений
RSYNC_EXCLUDES=()
for item in "${EXCLUDES[@]}"; do
    RSYNC_EXCLUDES+=( "--exclude=$item" )
done

# --- ФУНКЦИИ ---

make_backup() {
    echo "📂 [$(date +'%H:%M:%S')] Запуск резервного копирования..."
    
    mkdir -p "$BACKUP_PATH"

    # rsync копирует всё из SOURCE_DIR в BACKUP_PATH, применяя исключения
    # -a: архивный режим (сохраняет права, симлинки)
    # -v: подробный вывод
    rsync -av "${RSYNC_EXCLUDES[@]}" "$SOURCE_DIR/" "$BACKUP_PATH/"

    echo "📦 Упаковка в архив..."
    cd "$BACKUP_BASE_DIR" || exit
    tar -czf "${BACKUP_NAME}.tar.gz" "$BACKUP_NAME"
    
    echo "🧹 Очистка временных файлов..."
    rm -rf "$BACKUP_NAME"

    echo "✅ Бэкап создан: $BACKUP_BASE_DIR/${BACKUP_NAME}.tar.gz"

    # Удаление старых бэкапов (> 60 дней)
    find "$BACKUP_BASE_DIR" -type f -name "agentbot_backup_*.tar.gz" -mtime +60 -delete
}

restore_backup() {
    echo "🔄 [$(date +'%H:%M:%S')] Запуск восстановления..."

    # Поиск последнего архива
    LATEST=$(ls -t "$BACKUP_BASE_DIR"/agentbot_backup_*.tar.gz 2>/dev/null | head -n 1)

    if [ -z "$LATEST" ]; then
        echo "❌ Ошибка: Файлы бэкапа не найдены."
        exit 1
    fi

    echo "📦 Используется архив: $(basename "$LATEST")"
    
    TMP_DIR=$(mktemp -d)
    tar -xzf "$LATEST" -C "$TMP_DIR"

    # Внутри архива папка с именем agentbot_backup_TIMESTAMP
    # Находим её через find
    INNER_DIR=$(find "$TMP_DIR" -maxdepth 1 -type d -name "agentbot_backup_*" | head -n 1)

    if [ -d "$INNER_DIR" ]; then
        echo "🚚 Синхронизация файлов в $SOURCE_DIR..."
        # Используем rsync для восстановления (удалит лишнее в SOURCE_DIR, если добавить --delete)
        rsync -av "$INNER_DIR/" "$SOURCE_DIR/"
        echo "✅ Восстановление завершено."
    else
        echo "❌ Ошибка: Данные в архиве повреждены или имеют неверную структуру."
    fi

    rm -rf "$TMP_DIR"
}

# --- ЛОГИКА ЗАПУСКА ---
# Если переменная $1 пустая, мы присваиваем ей значение "make"
ACTION="${1:-make}"

case "$ACTION" in
    make)
        make_backup
        ;;
    restore)
        read -p "⚠️ Вы уверены, что хотите перезаписать файлы в $SOURCE_DIR? (y/n): " confirm
        if [[ $confirm == [yY] ]]; then
            restore_backup
        else
            echo "Отмена."
        fi
        ;;
    *)
        echo "Использование: $0 {make|restore} (по умолчанию: make)"
        exit 1
        ;;
esac