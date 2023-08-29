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
        private readonly IParserService _parserService;
        private readonly IBotService _botService;

        public AccountService(IMongoService mongoService, IParserService parserService, IBotService botService)
        {
            this._mongoService = mongoService;
            this._parserService = parserService;
            this._botService = botService;
        }

        public async Task<Models.User?> CreateAccount(User telegramUser)
        {
            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");

            var users = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList();
            if (users.Count >= 1) return null;
            
            var user = new Models.User(telegramUser.Id, telegramUser.Username, telegramUser.FirstName,
                telegramUser.LastName) {Id = ObjectId.GenerateNewId()};
         
            
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

            var correctGroupName = this._parserService.Groups.FirstOrDefault(group =>
                group.ToLower().Trim().Contains(groupName.ToLower().Trim()));
            
            if (correctGroupName is not { })
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id, "Группа не найдена"));
                return false;
            }

            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();

            user!.Group = correctGroupName;
            var update = Builders<Models.User>.Update.Set(u => u.Group, user.Group);
            await userCollection.UpdateOneAsync(u => u.UserId == telegramUser.Id, update);
            
            await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id, $"Вы успешно выбрали {correctGroupName} группу"));
            return true;
        }

        public async Task UpdateNotificationsStatus(User telegramUser)
        {
            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
            
            if (user is null) return;
            
            if (user.Group is null)
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id, $"Перед оформлением подписки на рассылку необходимо выбрать группу"));
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
                        user.Notifications ? new KeyboardButton("🙏Отписаться от рассылки🙏") : new KeyboardButton("💳Подписаться на рассылку💳")
                    }
                },
                ResizeKeyboard = true,
                InputFieldPlaceholder = "Выберите действие"
            };
            
            await this._botService.SendMessageAsync(new SendMessageArgs(telegramUser.Id, user.Notifications ? 
                $"Вы успешно подписались на расписание группы {user.Group}" :
                $"Вы успешно отменили подписку на расписание группы {user.Group}")
            {
                ReplyMarkup = keyboard
            });
        }
    }
}