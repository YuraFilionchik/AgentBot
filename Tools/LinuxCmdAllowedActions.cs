using System.Collections.Generic;

namespace AgentBot.Tools
{
    /// <summary>
    /// Конфигурация разрешённых действий для LinuxCMDTool.
    /// </summary>
    public static class LinuxCmdAllowedActions
    {
        /// <summary>
        /// Все разрешённые действия по умолчанию.
        /// </summary>
        public static readonly HashSet<string> DefaultActions = new()
        {
            // Чтение и просмотр
            "view_log",           // Просмотр логов (tail/cat)
            "read_file",          // Чтение файла (cat)
            "head_file",          // Первые строки файла (head)
            "tail_file",          // Последние строки файла (tail)
            "cat_file",           // Вывод содержимого (cat)
            
            // Поиск и анализ
            "grep",               // Поиск по шаблону (grep)
            "find",               // Поиск файлов (find)
            "wc",                 // Подсчёт строк/слов (wc)
            "sort",               // Сортировка (sort)
            "uniq",               // Удаление дубликатов (uniq)
            "diff",               // Сравнение файлов (diff)
            
            // Файловые операции
            "create_file",        // Создание файла (touch/echo)
            "edit_file",          // Добавление текста в конец файла (echo >>)
            "write_file",         // Перезапись содержимого файла целиком (tee)
            "replace_in_file",    // Замена строки/подстроки в файле (sed -i)
            "insert_line",        // Вставка текста на указанную строку (sed -i)
            "delete_file",        // Удаление файла (rm)
            "copy_file",          // Копирование (cp)
            "move_file",          // Перемещение (mv)
            "list_dir",           // Список файлов (ls)
            "make_dir",           // Создание директории (mkdir)
            "remove_dir",         // Удаление директории (rmdir)
            
            // Системные команды
            "service_status",     // Статус сервиса (systemctl/ps)
            "service_start",      // Запуск сервиса (systemctl start)
            "service_stop",       // Остановка сервиса (systemctl stop)
            "service_restart",    // Перезапуск сервиса (systemctl restart)
            "process_list",       // Список процессов (ps/top)
            "process_kill",       // Завершение процесса (kill)
            "disk_usage",         // Использование диска (df/du)
            "memory_usage",       // Использование памяти (free)
            "cpu_info",           // Информация о CPU (lscpu)
            "os_info",            // Информация об ОС (uname/cat /etc/os-release)
            "network_info",       // Сетевая информация (ip/ifconfig/netstat)
            "port_check",         // Проверка портов (ss/netstat)
            
            // Скрипты
            "run_script",         // Выполнение скрипта (bash)
            "run_python",         // Выполнение Python (python3)
            
            // Архивы
            "tar_create",         // Создание архива (tar czf)
            "tar_extract",        // Распаковка архива (tar xzf)
            "zip_create",         // Создание ZIP (zip)
            "zip_extract",        // Распаковка ZIP (unzip)
            
            // Логи и журналирование
            "journalctl",         // Просмотр systemd логов (journalctl)
            "dmesg",              // Сообщения ядра (dmesg)
            
            // Дата и время
            "date",               // Текущая дата (date)
            "uptime",             // Время работы системы (uptime)
            
            // Пользователи и права
            "whoami",             // Текущий пользователь (whoami)
            "pwd",                // Текущая директория (pwd)
            "file_info",          // Информация о файле (stat/file)
            
            // Сеть
            "ping",               // Проверка доступности (ping)
            "curl",               // HTTP запрос (curl)
            "wget",               // Загрузка файла (wget)
            "dns_lookup",         // DNS запрос (dig/nslookup)
            
            // Переменные окружения
            "env_list",           // Переменные окружения (env/printenv)
            "env_get",            // Получить переменную (echo $VAR)
        };

        /// <summary>
        /// Действия, разрешённые для выполнения через sudo.
        /// </summary>
        public static readonly HashSet<string> SudoAllowedActions = new()
        {
            // Системные сервисы
            "service_status",
            "service_start",
            "service_stop",
            "service_restart",
            
            // Скрипты
            "run_script",
            "run_python",
            
            // Логи
            "journalctl",
            "dmesg",
            
            // Сеть
            "network_info",
            "port_check",
            
            // Процессы
            "process_kill",
            
            // Дисковые операции
            "disk_usage",
            "memory_usage",
        };

        /// <summary>
        /// Запрещённые команды (никогда не разрешены).
        /// </summary>
        public static readonly HashSet<string> BannedCommands = new()
        {
            // Разрушительные команды
            "rm -rf /",
            "rm -rf /*",
            "dd if=/dev/zero",
            "mkfs",
            "mkfs.",
            
            // Перезагрузка и выключение
            "shutdown",
            "reboot",
            "halt",
            "poweroff",
            "init 0",
            "init 6",
            "telinit 0",
            "telinit 6",
            
            // Удаление пакетов
            "apt-get remove --purge .*",
            "yum erase .*",
            "dnf remove .*",
            "pacman -Rns .*",
            
            // Опасные изменения прав
            "chmod -R 777 /",
            "chmod -R 777 /*",
            "chown -R root:root /",
            "chown -R root:root /*",
            
            // Форматирование
            "fdisk",
            "mkswap",
            "swapon",
            "swapoff",
            
            // Запись в устройства
            "dd of=/dev/sda",
            "dd of=/dev/hda",
            "dd of=/dev/nvme",
            
            // Опасные скрипты
            ":(){ :|:& };:",  // Fork bomb
            "curl .* | bash",
            "wget .* | bash",
            
            // Изменение паролей
            "passwd root",
            "passwd .*",
            
            // История команд
            "history -c",
            "history -d",
            "rm .*bash_history",
            
            // SELinux/AppArmor
            "setenforce 0",
            "setenforce 1",
            
            // Монтирование
            "mount -o remount,ro /",
            "umount /",
        };

        /// <summary>
        /// Разрешённые директории по умолчанию.
        /// </summary>
        public static readonly List<string> DefaultDirectories = new()
        {
            "logs",
            "scripts",
            "data",
            "temp",
            "backup",
            "config",
            "reports",
            "exports",
        };
    }
}
