using MongoDB.Bson;
using MongoDB.Driver;
using StudentsTimetable.Config;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using User = Telegram.BotAPI.AvailableTypes.User;

namespace StudentsTimetable.Services
{
    public interface IAccountService
    {
        Task<Models.User?> CreateAccount(User telegramUser);
        Task<bool> ChangeGroup(User telegramUser, string? teacher);
        Task SubscribeNotifications(User telegramUser);
        Task UnSubscribeNotifications(User telegramUser);
    }

    public class AccountService : IAccountService
    {
        private readonly IMongoService _mongoService;
        private readonly IParserService _parserService;

        public AccountService(IMongoService mongoService, IParserService parserService)
        {
            this._mongoService = mongoService;
            this._parserService = parserService;
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

        public async Task<bool> ChangeGroup(User telegramUser, string? groupName)
        {
            if (groupName is null) return false;
            var config = new Config<MainConfig>();
            var bot = new BotClient(config.Entries.Token);
            
            string correctGroupName = string.Empty;
            foreach (var group in this._parserService.Groups)
            {
                if (!group.ToLower().Trim().Contains(groupName.ToLower().Trim())) continue;
                correctGroupName = group.Trim();
                break;
            }

            if (correctGroupName == string.Empty)
            {
                try
                {
                    await bot.SendMessageAsync(telegramUser.Id, $"Группа не найдена");
                    return false;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First() ?? await CreateAccount(telegramUser);

            user!.Group = correctGroupName;
            var update = Builders<Models.User>.Update.Set(u => u.Group, user.Group);
            await userCollection.UpdateOneAsync(u => u.UserId == telegramUser.Id, update);

            try
            {
                await bot.SendMessageAsync(telegramUser.Id, $"Вы успешно выбрали {correctGroupName} группу");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return true;
        }

        public async Task SubscribeNotifications(User telegramUser)
        {
            var config = new Config<MainConfig>();
            var bot = new BotClient(config.Entries.Token);

            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First() ?? await CreateAccount(telegramUser);
            
            if (user is null) return;
            if (user.Group is null)
            {
                try
                {
                    await bot.SendMessageAsync(telegramUser.Id, $"Перед оформлением подписки на рассылку необходимо выбрать группу");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

                return;
            }
            
            var update = Builders<Models.User>.Update.Set(u => u.Notifications, true);
            await userCollection.UpdateOneAsync(u => u.UserId == telegramUser.Id, update);
            
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
                       new KeyboardButton("Отписаться от рассылки") 
                    }
                },
                ResizeKeyboard = true,
                InputFieldPlaceholder = "Выберите действие"
            };
            
            try
            {
                await bot.SendMessageAsync(telegramUser.Id, $"Вы успешно подписались на расписание группы {user.Group}", replyMarkup: keyboard);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        
        public async Task UnSubscribeNotifications(User telegramUser)
        {
            var config = new Config<MainConfig>();
            var bot = new BotClient(config.Entries.Token);

            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First() ?? await CreateAccount(telegramUser);
            
            if (user is null) return;

            var update = Builders<Models.User>.Update.Set(u => u.Notifications, false);
            await userCollection.UpdateOneAsync(u => u.UserId == telegramUser.Id, update);
            
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
                        new KeyboardButton("Подписаться на рассылку")   
                    }
                },
                ResizeKeyboard = true,
                InputFieldPlaceholder = "Выберите действие"
            };
            
            try
            {
                await bot.SendMessageAsync(telegramUser.Id, $"Вы успешно отменили подписку на расписание", replyMarkup: keyboard);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}