using System.Text.RegularExpressions;
using MongoDB.Driver;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableMethods.FormattingOptions;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core.Services;
using File = System.IO.File;

namespace StudentsTimetable.Services
{
    public interface IInterfaceService
    {
        Task OpenMainMenu(Message message);
        Task NotifyAllUsers(Message message);
        Task SendWeek(User telegramUser);
        Task SendDayTimetable(User telegramUser);
        Task SendDayTimetable(Models.User? user);
    }

    public class InterfaceService : IInterfaceService
    {
        private readonly IMongoService _mongoService;
        private readonly IAccountService _accountService;
        private readonly IBotService _botService;

        private Dictionary<string, List<PhotoSize>> _photos = new();
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
            if (await this._accountService.GetUserById(sender.Id) is not { } user) return;

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

        public async Task NotifyAllUsers(Message message)
        {
            if (message.MediaGroupId is not null)
            {
                if (this._photos.TryGetValue(message.MediaGroupId, out var images))
                {
                    if (message.Photo is not null) images.Add(message.Photo.First());
                }
                else
                {
                    if (message.Photo is not null)
                        this._photos.Add(message.MediaGroupId, new List<PhotoSize>()
                        {
                            message.Photo.First()
                        });
                }
            }

            var (result, messageText) = this.ValidationAllRegexNotification(message);
            if (!result && message.Poll is null) return;

            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var users = (await userCollection.FindAsync(u => true)).ToList();
            if (users is null || users.Count <= 0) return;

            await Task.Delay(2000);

            var tasks = new List<Task>();
            List<InputMediaPhoto> mediaPhotos = new();

            if (message.MediaGroupId is not null)
            {
                foreach (var p in this._photos[message.MediaGroupId])
                {
                    mediaPhotos.Add(new InputMediaPhoto(p.FileId));
                }
            }


            foreach (var user in users)
            {
                tasks.Add(this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    $"📙Рассылка от бота📙: {messageText}")));
                try
                {
                    if (mediaPhotos.Count > 0)
                        tasks.Add(this._botService.BotClient.SendMediaGroupAsync(
                            new SendMediaGroupArgs(user.UserId, mediaPhotos)));

                    if (message.Poll is not null && message.From is not null)
                    {
                        tasks.Add(this._botService.BotClient.ForwardMessageAsync(user.UserId, message.From.Id,
                            message.MessageId));
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            await Task.WhenAll(tasks);
            if (message.MediaGroupId is not null) this._photos[message.MediaGroupId].Clear();
        }

        public async Task SendDayTimetable(User telegramUser)
        {
            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
            await this.SendDayTimetable(user);
        }

        public async Task SendWeek(User telegramUser)
        {
            var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
            var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
            if (user is null) return;

            if (user.Group is null || !File.Exists($"./cachedImages/{user.Group.Replace("*", "knor")}.png"))
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
                return;
            }

            var image = await Image.LoadAsync($"./cachedImages/{user.Group.Replace("*", "knor")}.png");

            if (image is not { })
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    "Увы, данная группа не найдена"));
                return;
            }

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);

            await this._botService.SendPhotoAsync(new SendPhotoArgs(user.UserId,
                new InputFile(ms.ToArray(), $"Group - {user.Group}")));
        }

        public async Task SendDayTimetable(Models.User? user)
        {
            if (user is null) return;

            if (user.Group is null)
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
                return;
            }

            if (ParseService.Timetable.Count < 1)
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    $"У {user.Group} группы нет пар"));
                return;
            }

            foreach (var day in ParseService.Timetable)
            {
                string message = day.Date + "\n";

                foreach (var groupInfo in day.GroupInfos.Where(groupInfo =>
                             int.Parse(user.Group.Replace("*", "")) == groupInfo.Number))
                {
                    if (groupInfo.Lessons.Count < 1)
                    {
                        message = $"У {groupInfo.Number} группы нет пар";
                        continue;
                    }

                    message = Utils.CreateDayTimetableMessage(groupInfo);
                }

                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    message.Trim().Length <= 1 ? "У вашей группы нет пар" : message)
                {
                    ParseMode = ParseMode.Markdown
                });
            }
        }

        private (bool result, string? messageText) ValidationAllRegexNotification(Message message)
        {
            var sayRegex = Match.Empty;

            if (message.Text is { } messageText)
            {
                sayRegex = SayRE.Match(messageText);
            }
            else if (message.Caption is { } msgCaption)
            {
                sayRegex = SayRE.Match(msgCaption);
            }

            return (sayRegex.Length > 0, sayRegex.Groups[1].Value.Trim());
        }
    }
}