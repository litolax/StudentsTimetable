using MongoDB.Bson;
using MongoDB.Driver;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core.Services;
using User = Telegram.BotAPI.AvailableTypes.User;

namespace StudentsTimetable.Services
{
    public interface IAccountService
    {
        Task<Models.User?> CreateAccount(User telegramUser);
        Task<bool> ChangeGroup(User telegramUser, string? teacher);
        Task UpdateNotificationsStatus(User telegramUser);
        Task<Models.User?> GetUserById(long id);
    }

    public class AccountService : IAccountService
    {
        private readonly IMongoService _mongoService;
        private readonly IParseService _parseService;
        private readonly IBotService _botService;
        private const int MaxGroupCount = 5;
        public AccountService(IMongoService mongoService, IParseService parseService, IBotService botService)
        {
            this._mongoService = mongoService;
            this._parseService = parseService;
            this._botService = botService;
        }

        public async Task<Models.User?> CreateAccount(User telegramUser)
        {
            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");

            var users = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList();
            if (users.Count >= 1) return null;

            var user = new Models.User(telegramUser.Id, telegramUser.Username, telegramUser.FirstName,
                telegramUser.LastName) { Id = ObjectId.GenerateNewId() };


            await userCollection.InsertOneAsync(user);
            return user;
        }

        public async Task<Models.User?> GetUserById(long id)
        {
            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            return (await userCollection.FindAsync(u => u.UserId == id)).FirstOrDefault();
        }

        public async Task<bool> ChangeGroup(User telegramUser, string? groupName)
        {
            if (groupName is null) return false;
            var groupNames = groupName.Split(',', ';', StringSplitOptions.RemoveEmptyEntries);
            groupNames = groupNames.Length > MaxGroupCount ? groupNames[..MaxGroupCount] : groupNames;
            for (var i = 0; i < groupNames.Length; i++)
            {
                groupNames[i] = groupNames[i].Trim();
            }

            var correctGroupNames = this._parseService.Groups.Where(g => groupNames.Any(group =>
                g.ToLower().Trim().Contains(group.ToLower().Trim()))).ToArray();

            if (correctGroupNames is null || correctGroupNames.Length == 0)
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id,
                    $"Групп{(groupNames.Length == 0 ? 'a' : 'ы')} {Utils.GetGroupsString(groupNames)} не найден{(groupNames.Length == 0 ? 'a' : 'ы')}"));
                return false;
            }

            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();

            user!.Groups = correctGroupNames;
            var update = Builders<Models.User>.Update.Set(u => u.Groups, user.Groups);
            await userCollection.UpdateOneAsync(u => u.UserId == telegramUser.Id, update);

            await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id,
                correctGroupNames.Length == 1
                    ? $"Вы успешно выбрали {correctGroupNames[0]} группу"
                    : $"Вы успешно выбрали группы {Utils.GetGroupsString(correctGroupNames)}"));
            return true;
        }

        public async Task UpdateNotificationsStatus(User telegramUser)
        {
            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();

            if (user is null) return;

            if (user.Groups is null)
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id,
                    $"Перед оформлением подписки на рассылку необходимо выбрать группу"));
                return;
            }

            user.Notifications = !user.Notifications;
            var update = Builders<Models.User>.Update.Set(u => u.Notifications, user.Notifications);
            await userCollection.UpdateOneAsync(u => u.UserId == telegramUser.Id, update);

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

            await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id,
                user.Notifications
                    ? $"Вы успешно подписались на расписание группы {user.Groups}"
                    : $"Вы успешно отменили подписку на расписание группы {user.Groups}")
            {
                ReplyMarkup = keyboard
            });
        }
    }
}