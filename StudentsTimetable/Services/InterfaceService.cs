using System.Text.RegularExpressions;
using MongoDB.Driver;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using TelegramBot_Timetable_Core.Config;
using TelegramBot_Timetable_Core.Services;
using User = StudentsTimetable.Models.User;

namespace StudentsTimetable.Services
{
    public interface IInterfaceService
    {
        Task OpenMainMenu(Message message);
        Task NotifyAllUsers(Message message);
    }

    public class InterfaceService : IInterfaceService
    {
        private readonly IMongoService _mongoService;
        private readonly IAccountService _accountService;
        private readonly IBotService _botService;

        private static readonly Regex SayRE = new(@"\/sayall(.+)", RegexOptions.Compiled);

        public InterfaceService(IMongoService mongoService, IAccountService accountService, IBotService botService)
        {
            this._mongoService = mongoService;
            this._accountService = accountService;
            this._botService = botService;
        }

        public async Task OpenMainMenu(Message message)
        {
            if (message.From is not { } sender) return;

            var userCollection = this._mongoService.Database.GetCollection<User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == sender.Id)).FirstOrDefault() ??
                       await this._accountService.CreateAccount(sender);

            if (user is null) return;

            var keyboard = new ReplyKeyboardMarkup
            {
                Keyboard = new[]
                {
                    new[]
                    {
                        new KeyboardButton("🎰Посмотреть расписание на день🎰"),
                    },
                    new[]
                    {
                        new KeyboardButton("🔪Посмотреть расписание на неделю🔪"),
                    },
                    new[]
                    {
                        new KeyboardButton("👨‍👨‍👧‍👦Сменить группу👨‍👨‍👧‍👦"),
                    },
                    new[]
                    {
                        user.Notifications
                            ? new KeyboardButton("🙏Отписаться от рассылки🙏")
                            : new KeyboardButton("💳Подписаться на рассылку💳")
                    }
                },
                ResizeKeyboard = true,
                InputFieldPlaceholder = "Выберите действие"
            };

            this._botService.SendMessage(new SendMessageArgs(sender.Id, "Вы открыли меню.")
            {
                ReplyMarkup = keyboard
            });
        }

        public async Task NotifyAllUsers(Message msg)
        {
            var sayRegex = Match.Empty;

            if (msg.Text is { } messageText)
            {
                sayRegex = SayRE.Match(messageText);
            }
            else if (msg.Caption is { } msgCaption)
            {
                sayRegex = SayRE.Match(msgCaption);
            }

            if (sayRegex.Length <= 0) return;

            var message = sayRegex.Groups[1].Value.Trim();

            var userCollection = this._mongoService.Database.GetCollection<User>("Users");
            var users = (await userCollection.FindAsync(u => true)).ToList();
            if (users is null || users.Count <= 0) return;

            var tasks = new List<Task>();

            foreach (var user in users)
            {
                tasks.Add(this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, $"Уведомление: {message}")));

                // if (msg.Photo is null) continue;
                // foreach (var photo in msg.Photo)
                // {
                //     tasks.Add(this._botService.SendPhotoAsync(new SendPhotoArgs(user.UserId, photo.FileId)));
                // }
            }

            await Task.WhenAll(tasks);
        }
    }
}