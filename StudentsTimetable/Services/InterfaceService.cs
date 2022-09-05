using System.Text.RegularExpressions;
using MongoDB.Driver;
using StudentsTimetable.Config;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using User = StudentsTimetable.Models.User;

namespace StudentsTimetable.Services
{
    public interface IInterfaceService
    {
        Task OpenMainMenu(Update update);
        Task NotifyAllUsers(Update update);
        Task HelpCommand(Telegram.BotAPI.AvailableTypes.User telegramUser);
    }

    public class InterfaceService : IInterfaceService
    {
        private readonly IMongoService _mongoService;
        private readonly IAccountService _accountService;

        private static readonly Regex SayRE = new(@"\/sayall(.+)", RegexOptions.Compiled);

        public InterfaceService(IMongoService mongoService, IAccountService accountService)
        {
            this._mongoService = mongoService;
            this._accountService = accountService;
        }

        public async Task OpenMainMenu(Update update)
        {
            var config = new Config<MainConfig>();
            var bot = new BotClient(config.Entries.Token);

            var userCollection = this._mongoService.Database.GetCollection<User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == update.Message.From!.Id)).FirstOrDefault();
            if (user == default) user = await this._accountService.CreateAccount(update.Message.From!);

            var keyboard = new ReplyKeyboardMarkup
            {
                Keyboard = new[]
                {
                    new[]
                    {
                        new KeyboardButton("Посмотреть расписание на день"),
                    },
                    new[]
                    {
                        new KeyboardButton("Посмотреть расписание на неделю"),
                    },
                    new[]
                    {
                        new KeyboardButton("Сменить группу"),
                    },
                    new[]
                    {
                        user!.Notifications
                            ? new KeyboardButton("Отписаться от рассылки")
                            : new KeyboardButton("Подписаться на рассылку")
                    }
                },
                ResizeKeyboard = true,
                InputFieldPlaceholder = "Выберите действие"
            };

            await bot.SendMessageAsync(update.Message.From!.Id, "Вы открыли меню.", replyMarkup: keyboard);
        }

        public async Task NotifyAllUsers(Update update)
        {
            var sayRegex = SayRE.Match(update.Message.Text!);
            if (sayRegex.Length <= 0) return;

            var message = sayRegex.Groups[1].Value.Trim();

            var config = new Config<MainConfig>();
            var bot = new BotClient(config.Entries.Token);

            var userCollection = this._mongoService.Database.GetCollection<User>("Users");
            var users = (await userCollection.FindAsync(u => true)).ToList();
            if (users is null || users.Count <= 0) return;

            foreach (var user in users)
            {
                try
                {
                    await bot.SendMessageAsync(user.UserId, $"Уведомление: {message}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public async Task HelpCommand(Telegram.BotAPI.AvailableTypes.User telegramUser)
        {
            var config = new Config<MainConfig>();
            var bot = new BotClient(config.Entries.Token);
            
            try
            {
                await bot.SendMessageAsync(telegramUser.Id, $"Вы пользуетесь ботом, который поможет узнать Вам актуальное расписание студентов МГКЭ.\nСоздатель @litolax");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}