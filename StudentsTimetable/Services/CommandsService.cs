using System.Text.RegularExpressions;
using MongoDB.Driver;
using StudentsTimetable.Config;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableMethods.FormattingOptions;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core;
using TelegramBot_Timetable_Core.Config;
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
        private readonly IMongoService _mongoService;
        private readonly IBotService _botService;
        private readonly IDistributionService _distributionService;
        private static string _groupsList;

        public CommandsService(IInterfaceService interfaceService, IAccountService accountService,
            IMongoService mongoService, IBotService botService, IDistributionService distributionService)
        {
            Core.OnMessageReceive += this.OnMessageReceive;
            _groupsList = string.Join('\n', new Config<GroupsConfig>().Entries.Groups);
            this._interfaceService = interfaceService;
            this._accountService = accountService;
            this._mongoService = mongoService;
            this._botService = botService;
            this._distributionService = distributionService;
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
                    if (await this._accountService.GetUserById(sender.Id) is null)
                        await this._accountService.CreateAccount(sender);

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
                case "/groups":
                {
                    this._botService.SendMessage(new SendMessageArgs(sender.Id, _groupsList));
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
                    // this._botService.SendMessage(new SendMessageArgs(sender.Id, "Данная функция временно недоступна"));
                    await this._distributionService.SendDayTimetable(sender);
                    break;
                }
                case "🔪Посмотреть расписание на неделю🔪":
                {
                    await this._distributionService.SendWeek(sender);
                    break;
                }
                case "👨‍👨‍👧‍👦Сменить группу👨‍👨‍👧‍👦":
                {
                    this._botService.SendMessage(
                        new SendMessageArgs(sender.Id,
                            $"Для выбора групп отправьте её номера.(Максимум - 5 групп. Пример: 160, 161, 166,53, 54)\nВаши выбранные группы:📑```\n{Utils.GetGroupsString((await this._accountService.GetUserById(sender.Id))?.Groups ?? Array.Empty<string>())}\n```")
                        {
                            ParseMode = ParseMode.Markdown
                        });
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

                    if (lowerMessageText.Contains("/timetablenotify"))
                    {
                        var notificationUsers = new List<Models.User>();
                        notificationUsers.AddRange(
                            (await this._mongoService.Database.GetCollection<Models.User>("Users")
                                .FindAsync(u => u.Groups != null && u.Notifications)).ToList());

                        if (notificationUsers.Count == 0) return;

                        _ = Task.Run(() =>
                        {
                            foreach (var user in notificationUsers)
                            {
                                _ = this._distributionService.SendDayTimetable(user);
                            }

                            this._botService.SendAdminMessageAsync(new SendMessageArgs(0,
                                $"After timetablenotify:{notificationUsers.Count} notifications sent"));
                        });
                    }
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