using MongoDB.Driver;
using StudentsTimetable.Config;
using StudentsTimetable.Models;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.GettingUpdates;

namespace StudentsTimetable.Services
{
    public interface ICommandsService
    {
        Task CommandsValidator(Update update);
    }

    public class CommandsService : ICommandsService
    {
        private readonly IInterfaceService _interfaceService;
        private readonly IAccountService _accountService;
        private readonly IParserService _parserService;
        private readonly IMongoService _mongoService;

        public CommandsService(IInterfaceService interfaceService, IAccountService accountService, IParserService parserService, IMongoService mongoService)
        {
            this._interfaceService = interfaceService;
            this._accountService = accountService;
            this._parserService = parserService;
            this._mongoService = mongoService;
        }

        public async Task CommandsValidator(Update update)
        {
            var lastState = await this._mongoService.GetLastState(update.Message.Chat.Id);
            if (lastState is not null && lastState == "changeGroup")
            {
                var result = await this._accountService.ChangeGroup(update.Message.From!, update.Message.Text);
                if (result) this._mongoService.RemoveState(update.Message.Chat.Id);
            }
            
            
            switch (update.Message.Text)
            {
                case "/start":
                {
                    await this._interfaceService.OpenMainMenu(update);
                    var config = new Config<MainConfig>();
                    var bot = new BotClient(config.Entries.Token);
                    try
                    {
                        await bot.SendMessageAsync(update.Message.From!.Id, $"Используя бота вы подтверждаете, " +
                                                                            $"что автор не несет за вас и ваши действия никакой ответственности");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    break;
                }
                case "/menu":
                {
                    await this._interfaceService.OpenMainMenu(update);
                    break;
                }
                case "/help":
                {
                    if (update.Message.From is null) return;
                    await this._interfaceService.HelpCommand(update.Message.From);
                    break;
                }
                case "/tos":
                {
                    var config = new Config<MainConfig>();
                    var bot = new BotClient(config.Entries.Token);
                    try
                    {
                        await bot.SendMessageAsync(update.Message.From!.Id, $"Используя бота вы подтверждаете, " +
                                                                            $"что автор не несет за вас и ваши действия никакой ответственности");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    break;
                }
                case "🎰Посмотреть расписание на день🎰":
                {
                    if (update.Message.From is null) return;
                    await this._parserService.SendDayTimetable(update.Message.From);
                    break;
                }
                case "🔪Посмотреть расписание на неделю🔪":
                {
                    if (update.Message.From is null) return;
                    await this._parserService.SendWeekTimetable(update.Message.From);
                    break;
                }
                case "👨‍👨‍👧‍👦Сменить группу👨‍👨‍👧‍👦":
                {
                    var config = new Config<MainConfig>();
                    var bot = new BotClient(config.Entries.Token);
                    try
                    {
                        await bot.SendMessageAsync(update.Message.From!.Id, $"Для выбора группы отправьте её номер.");
                        this._mongoService.CreateState(new UserState(update.Message.Chat.Id, "changeGroup"));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    break;
                }
                case "💳Подписаться на рассылку💳":
                {
                    if (update.Message.From is null) return;
                    await this._accountService.SubscribeNotifications(update.Message.From);
                    break;
                }
                case "🙏Отписаться от рассылки🙏":
                {
                    if (update.Message.From is null) return;
                    await this._accountService.UnSubscribeNotifications(update.Message.From);
                    break;
                }
            }

            if (update.Message.Text!.ToLower().Contains("/sayall") && update.Message.From!.Id == 698346968)
                await this._interfaceService.NotifyAllUsers(update);

            if (update.Message.Text!.ToLower().Contains("/stopparse") && update.Message.From!.Id == 698346968)
            {
                var info = (await this._mongoService.Database.GetCollection<Info>("Info").FindAsync(i => true)).ToList().First();
                info.ParseAllowed = false;
                var infoUpdate = Builders<Info>.Update.Set(i => i.ParseAllowed, false);
                await this._mongoService.Database.GetCollection<Info>("Info").UpdateOneAsync(i => i.Id == info.Id, infoUpdate);
            }

            if (update.Message.Text!.ToLower().Contains("/startparse") && update.Message.From!.Id == 698346968)
            {
                var info = (await this._mongoService.Database.GetCollection<Info>("Info").FindAsync(i => true)).ToList().First();
                info.ParseAllowed = true;
                var infoUpdate = Builders<Info>.Update.Set(i => i.ParseAllowed, true);
                await this._mongoService.Database.GetCollection<Info>("Info").UpdateOneAsync(i => i.Id == info.Id, infoUpdate);
                await this._parserService.ParseDayTimetables();
            }

            if (update.Message.Text!.ToLower().Contains("/unload") && update.Message.From!.Id == 698346968)
            {
                var info = (await this._mongoService.Database.GetCollection<Info>("Info").FindAsync(i => true)).ToList().First();
                info.LoadFixFile = false;
                var infoUpdate = Builders<Info>.Update.Set(i => i.LoadFixFile, false);
                await this._mongoService.Database.GetCollection<Info>("Info").UpdateOneAsync(i => i.Id == info.Id, infoUpdate);
            }

            if (update.Message.Text!.ToLower().Contains("/load") && update.Message.From!.Id == 698346968)
            {
                var info = (await this._mongoService.Database.GetCollection<Info>("Info").FindAsync(i => true)).ToList().First();
                info.LoadFixFile = true;
                var infoUpdate = Builders<Info>.Update.Set(i => i.LoadFixFile, true);
                await this._mongoService.Database.GetCollection<Info>("Info").UpdateOneAsync(i => i.Id == info.Id, infoUpdate);
            }

            if (update.Message.Text!.ToLower().Contains("/notify") && update.Message.From!.Id == 698346968)
                await this._parserService.SendNewDayTimetables();
        }
    }
}