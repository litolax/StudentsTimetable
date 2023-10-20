using MongoDB.Driver;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableMethods.FormattingOptions;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core.Services;
using File = System.IO.File;

namespace StudentsTimetable.Services;

public interface IDistributionService
{
    Task SendDayTimetable(User telegramUser);
    Task SendWeek(User telegramUser);
    Task SendDayTimetable(Models.User? user);
}

public class DistributionService : IDistributionService
{
    private readonly IBotService _botService;
    private readonly IMongoService _mongoService;

    public DistributionService(IBotService botService, IMongoService mongoService)
    {
        this._botService = botService;
        this._mongoService = mongoService;
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
        if (user.Groups is null)
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        foreach (var group in user.Groups)
        {
            if (group is null || !File.Exists($"./cachedImages/{group.Replace("*", "knor")}.png")) return;
            var image = await Image.LoadAsync($"./cachedImages/{group.Replace("*", "knor")}.png");

            if (image is not { })
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    "Увы, данная группа не найдена"));
                return;
            }

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);

            await this._botService.SendPhotoAsync(new SendPhotoArgs(user.UserId,
                new InputFile(ms.ToArray(), $"Group - {user.Groups}")));
        }
    }

    public async Task SendDayTimetable(Models.User? user)
    {
        if (user is null) return;
        if (user.Groups is null)
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        foreach (var group in user.Groups)
        {
            if (group is null) continue;
            if (ParseService.Timetable.Count < 1)
            {
                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    $"У {user.Groups} группы нет пар"));
                return;
            }

            foreach (var day in ParseService.Timetable)
            {
                var message = string.Empty;

                foreach (var groupInfo in day.GroupInfos.Where(groupInfo =>
                             int.Parse(group?.Replace("*", "") ?? string.Empty) == groupInfo.Number))
                {
                    if (groupInfo.Lessons.Count < 1)
                    {
                        message = $"У {groupInfo.Number} группы нет пар";
                        continue;
                    }

                    message = $"День - {day.Date}\n" + Utils.CreateDayTimetableMessage(groupInfo);
                }

                await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    message.Trim().Length <= 1 ? "У вашей группы нет пар" : message)
                {
                    ParseMode = ParseMode.Markdown
                });
            }
        }
    }
}