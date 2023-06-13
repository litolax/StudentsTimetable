using Microsoft.Extensions.DependencyInjection;
using StudentsTimetable.Services;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core;
using TelegramBot_Timetable_Core.Config;
using TelegramBot_Timetable_Core.Services;

namespace StudentsTimetable
{
    class Program
    {
        private static void Main()
        {
            Run().GetAwaiter().GetResult();
        }

        private static async Task Run()
        {
            var serviceProvider = new ServiceCollection()
                .AddSingleton<IMongoService, MongoService>()
                .AddSingleton<IBotService, BotService>()
                .AddSingleton<IParserService, ParserService>()
                .AddSingleton<ICommandsService, CommandsService>()
                .AddSingleton<IInterfaceService, InterfaceService>()
                .AddSingleton<IAccountService, AccountService>()
                .AddSingleton(typeof(IConfig<>), typeof(Config<>))
                .BuildServiceProvider(true);

            serviceProvider.GetService<ICommandsService>();
            var parserService = serviceProvider.GetService<IParserService>()!;

            try
            {
                await parserService.ParseWeekTimetables();
                //await parserService.ParseDayTimetables(true);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            await Core.Start(new[]
            {
                new BotCommand("start", "Запустить приложение"), new BotCommand("help", "Помощь"),
                new BotCommand("belltime", "Посмотреть расписание звонков")
            });
        }
    }
}