using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core;
using TelegramBot_Timetable_Core.Models;
using TelegramBot_Timetable_Core.Services;

namespace StudentsTimetable.Services
{
    public interface ICommandsService
    {
    }

    public class CommandsService : ICommandsService
    {
        private readonly IInterfaceService _interfaceService;
        private readonly IAccountService _accountService;
        private readonly IParserService _parserService;
        private readonly IMongoService _mongoService;
        private readonly IBotService _botService;

        public CommandsService(IInterfaceService interfaceService, IAccountService accountService,
            IParserService parserService, IMongoService mongoService, IBotService botService)
        {
            Core.OnMessageReceive += this.OnMessageReceive;
            
            this._interfaceService = interfaceService;
            this._accountService = accountService;
            this._parserService = parserService;
            this._mongoService = mongoService;
            this._botService = botService;
        }

        private async void OnMessageReceive(Message message)
        {
            if (message.From is not { } sender) return;
            var messageText = message.Text;
            
            var lastState = await this._mongoService.GetLastState(message.Chat.Id);
            if (lastState is not null && lastState == "changeGroup")
            {
                var result = await this._accountService.ChangeGroup(sender, messageText);
                if (result) this._mongoService.RemoveState(message.Chat.Id);
            }

            
            switch (messageText)
            {
                case "/start":
                {
                    if (await this._accountService.GetUserById(sender.Id) is null) await this._accountService.CreateAccount(sender);
                    
                    await this._interfaceService.OpenMainMenu(message);
                    this._botService.SendMessage(new SendMessageArgs(sender.Id,
                        "Используя бота вы подтверждаете, что автор не несет за вас и ваши действия никакой ответственности"));
                    break;
                }
                case "/help":
                {
                    this._botService.SendMessage(new SendMessageArgs(sender.Id,
                        $"Вы пользуетесь ботом, который поможет узнать Вам актуальное расписание учеников МГКЦТ.\nСоздатель @litolax"));
                    break;
                }
                case "/belltime":
                {
                    this._botService.SendMessage(new SendMessageArgs(sender.Id, $"""
    Расписание звонков: 
    
Будние дни: 
    
1) 09:00 - 09:45 | 09:55 - 10:40
2) 10:50 - 11:35 | 11:55 - 12:40
3) 13:00 - 13:45 | 13:55 - 14:40
4) 14:50 - 15:35 | 15:45 - 16:30
5) 16:40 - 17:25 | 17:35 - 18:20
6) 18:30 - 19:15 | 19:25 - 20:10

Суббота:  
    
1) 09:00 - 09:45 | 09:55 - 10:40
2) 10:50 - 11:35 | 11:50 - 12:35
3) 12:50 - 13:35 | 13:45 - 14:30
4) 14:40 - 15:25 | 15:35 - 16:20
5) 16:30 - 17:15 | 17:25 - 18:10
6) 18:20 - 19:05 | 19:15 - 20:00
"""));
                    break;
                }
                case "🎰Посмотреть расписание на день🎰":
                {
                    //this._botService.SendMessage(new SendMessageArgs(sender.Id, "Данная функция временно недоступна"));
                    await this._parserService.SendDayTimetable(sender);
                    break;
                }
                case "🔪Посмотреть расписание на неделю🔪":
                {
                    await this._parserService.SendWeek(sender);
                    break;
                }
                case "👨‍👨‍👧‍👦Сменить группу👨‍👨‍👧‍👦":
                {
                    this._botService.SendMessage(new SendMessageArgs(sender.Id, "Для выбора группы отправьте её номер."));
                    this._mongoService.CreateState(new UserState(message.Chat.Id, "changeGroup"));
                    break;
                }
                case "💳Подписаться на рассылку💳":
                case "🙏Отписаться от рассылки🙏":
                {
                    await this._accountService.UpdateNotificationsStatus(sender);
                    break;
                }
            }

            try
            {
                if (!Core.Administrators.Contains(sender.Id)) return;

                if (messageText is not null)
                {
                    var lowerMessageText = messageText.ToLower();
                    
                   // if (lowerMessageText.Contains("/notify"))
                       // await this._parserService.SendNewDayTimetables();
                }
                
                await this._interfaceService.NotifyAllUsers(message);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}