using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace AgentBot.Handlers
{
    internal class MessageProcessor
    {
        private readonly CommandHandler _commandHandler;
        private readonly IAiAgent _aiAgent;
        private readonly IBotProvider _botProvider;
        private readonly ILogger<MessageProcessor> _logger;
        private readonly List<IToolFunction> _tools; // Инжектируйте из DI

        public MessageProcessor(/* Инъекции: CommandHandler, IAiAgent, IBotProvider, ILogger, IEnumerable<IToolFunction> tools */)
        {
            // Инициализация
        }

        public async Task ProcessAsync(Message message)
        {
            // Логика: если message.Text.StartsWith("/"), вызов CommandHandler; иначе IAiAgent.ProcessMessageAsync; затем _botProvider.SendMessageAsync
        }
    }
}
