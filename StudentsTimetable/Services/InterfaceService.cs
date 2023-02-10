using System.Text.RegularExpressions;
using MongoDB.Driver;
using StudentsTimetable.Models;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using TelegramBot_Timetable_Core.Config;
using TelegramBot_Timetable_Core.Models;
using TelegramBot_Timetable_Core.Services;

namespace StudentsTimetable.Services
{
    public interface IInterfaceService
    {
        Task OpenMainMenu(Message message);
    }

    public class InterfaceService : IInterfaceService
    {
        private readonly IMongoService _mongoService;
        private readonly IAccountService _accountService;
        private readonly IBotService _botService;

        public InterfaceService(IMongoService mongoService, IAccountService accountService, IBotService botService)
        {
            this._mongoService = mongoService;
            this._accountService = accountService;
            this._botService = botService;
        }

        public async Task OpenMainMenu(Message message)
        {
            if (message.From is not { } sender) return;

            var userCollection = this._mongoService.Database.GetCollection<StudentsUser>("Users");
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
    }
}