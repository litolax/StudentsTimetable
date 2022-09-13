using Microsoft.Extensions.DependencyInjection;
using StudentsTimetable.Config;
using StudentsTimetable.Services;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;

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
                .AddSingleton<IParserService, ParserService>()
                .AddSingleton<ICommandsService, CommandsService>()
                .AddSingleton<IInterfaceService, InterfaceService>()
                .AddSingleton<IAccountService, AccountService>()
                .AddSingleton<IAntiSpamService, AntiSpamService>()
                .AddSingleton(typeof(IConfig<>), typeof(Config<>))
                .BuildServiceProvider(true);

            var parserService = serviceProvider.GetService<IParserService>();
            var commandsService = serviceProvider.GetService<ICommandsService>();
            var antiSpamService = serviceProvider.GetService<IAntiSpamService>();

            if (commandsService is null || parserService is null || antiSpamService is null) return;
            await parserService.ParseDayTimetables();
            await parserService.ParseWeekTimetables();

            var mainConfig = new Config<MainConfig>();
            var bot = new BotClient(mainConfig.Entries.Token);
            var updates = await bot.GetUpdatesAsync();
            bot.SetMyCommands(new[]
            {
                new BotCommand("start", "Запустить приложение"), new BotCommand("help", "Помощь"),
                new BotCommand("menu", "Открыть меню"), new BotCommand("tos", "Пользовательское соглашение")
            });

            Console.WriteLine("Bot started!");

            while (true)
            {
                if (updates.Any())
                {
                    foreach (var update in updates)
                    {
                        try
                        {
                            switch (update.Type)
                            {
                                case UpdateType.Message:
                                    if (update.Message.Date < DateTimeOffset.UtcNow.AddMinutes(-3).ToUnixTimeSeconds())
                                        continue;
                                    if (update.Message.From is null || update.Message.From.IsBot) continue;

                                    if (await antiSpamService.IsSpammer(update.Message.From.Id)) continue;
                                    if (updates.Count(u => u.Message.From!.Id == update.Message.From.Id) >= 5)
                                    {
                                        await antiSpamService.AddToSpam(update.Message.From.Id);
                                        await bot.SendMessageAsync(update.Message.From.Id,
                                            "Вы были добавлены в спам лист на 2 минуты. Не переживайте, передохните, и попробуйте еще раз");
                                        continue;
                                    }

                                    await commandsService.CommandsValidator(update);
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }

                    var offset = updates.Last().UpdateId + 1;
                    updates = await bot.GetUpdatesAsync(offset);
                }
                else
                {
                    updates = await bot.GetUpdatesAsync();
                }
            }
        }
    }
}