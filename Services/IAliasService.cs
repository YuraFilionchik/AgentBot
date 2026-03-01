using System.Collections.Generic;
using System.Threading.Tasks;
using AgentBot.Models;

namespace AgentBot.Services
{
    /// <summary>
    /// Сервис управления алиасами и базой знаний пользователей.
    /// </summary>
    public interface IAliasService
    {
        /// <summary>
        /// Добавляет новый алиас для пользователя.
        /// </summary>
        Task<Alias> AddAliasAsync(long userId, string aliasName, string value, AliasType type);

        /// <summary>
        /// Удаляет алиас по имени.
        /// </summary>
        Task<bool> DeleteAliasAsync(long userId, string aliasName);

        /// <summary>
        /// Получает все алиасы пользователя.
        /// </summary>
        Task<List<Alias>> GetAllAliasesAsync(long userId);

        /// <summary>
        /// Получает алиас по имени.
        /// </summary>
        Task<Alias?> GetAliasByNameAsync(long userId, string aliasName);

        /// <summary>
        /// Получает все алиасы команд пользователя.
        /// </summary>
        Task<List<Alias>> GetCommandAliasesAsync(long userId);

        /// <summary>
        /// Получает все алиасы знаний пользователя.
        /// </summary>
        Task<List<Alias>> GetKnowledgeAliasesAsync(long userId);

        /// <summary>
        /// Формирует контекстное описание алиасов для ИИ.
        /// </summary>
        Task<string> GetAliasesContextAsync(long userId);

        /// <summary>
        /// Проверяет, является ли слово алиасом команды, и возвращает команду.
        /// </summary>
        Task<string?> ResolveCommandAliasAsync(string text);
    }
}
