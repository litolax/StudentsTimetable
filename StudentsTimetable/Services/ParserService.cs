using HtmlAgilityPack;
using MongoDB.Driver;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using StudentsTimetable.Models;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableMethods.FormattingOptions;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core.Config;
using TelegramBot_Timetable_Core.Services;
using Timer = System.Timers.Timer;
using User = Telegram.BotAPI.AvailableTypes.User;

namespace StudentsTimetable.Services;

public interface IParserService
{
    List<string> Groups { get; set; }
    Task ParseWeekTimetables();
    Task ParseDayTimetables();
    Task SendWeekTimetable(User telegramUser);
    Task SendDayTimetable(User telegramUser);
}

public class ParserService : IParserService
{
    private readonly IMongoService _mongoService;
    private readonly IBotService _botService;
    private readonly IConfig<MainConfig> _config;

    private const string WeekUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-неделю";
    private const string DayUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-день";

    private string LastDayHtmlContent { get; set; }
    private string LastWeekHtmlContent { get; set; }

    private bool _weekParseStarted = false;
    private bool _dayParseStarted = false;

    public List<string> Groups { get; set; } = new()
    {
        "7", "8", "41", "42", "43", "44", "45", "46", "48", "49", "50", "51",
        "52", "53", "54", "55", "56", "57", "58", "60", "61", "62", "63", "64", "65",
        "66", "67", "68", "69", "70", "71", "72", "73", "74", "75",
        "76", "77", "78"
    };

    public List<Day> TempTimetable { get; set; } = new();
    public List<Day> Timetable { get; set; } = new();

    public ParserService(IMongoService mongoService, IBotService botService, IConfig<MainConfig> config)
    {
        this._mongoService = mongoService;
        this._botService = botService;
        this._config = config;

        var parseDayTimer = new Timer(150_000)
        {
            AutoReset = true, Enabled = true
        };
        parseDayTimer.Elapsed += (sender, args) =>
        {
            _ = this.NewDayTimetableCheck()
                .ContinueWith((t) => { Console.WriteLine(t.Exception?.InnerException); },
                    TaskContinuationOptions.OnlyOnFaulted);
        };

        var parseWeekTimer = new Timer(200_000)
        {
            AutoReset = true, Enabled = true
        };
        parseWeekTimer.Elapsed += (sender, args) =>
        {
            _ = this.NewWeekTimetableCheck()
                .ContinueWith((t) => { Console.WriteLine(t.Exception?.InnerException); },
                    TaskContinuationOptions.OnlyOnFaulted);
        };
    }

    public async Task ParseDayTimetables()
    {
        if (this._dayParseStarted) return;
        this._dayParseStarted = true;

        var driver = Utils.CreateChromeDriver();
        driver.Manage().Timeouts().PageLoad = new TimeSpan(0, 0, 20);

        driver.Navigate().GoToUrl(DayUrl);

        var content = driver.FindElement(By.Id("wrapperTables"));
        
        if (content is null)
        {
            this._dayParseStarted = false;
            driver.Dispose();
            return;
        }

        this.LastDayHtmlContent = content.Text;
        List<GroupInfo> groupInfos = new List<GroupInfo>();
        this.TempTimetable.Clear();

        var groupsAndLessons = content.FindElements(By.XPath(".//div")).ToList();

        foreach (var group in this.Groups)
        {
            try
            {
                for (var i = 1; i < groupsAndLessons.Count; i += 2)
                {
                    if (groupsAndLessons[i - 1].Text.Split('-')[0].Trim() != group) continue;
                    GroupInfo groupInfo = new GroupInfo();
                    List<Lesson> lessons = new List<Lesson>();

                    var lessonsElements = groupsAndLessons[i].FindElements(By.XPath(".//table/tbody/tr")).ToList();

                    if (lessonsElements.Count < 1)
                    {
                        groupInfo.Lessons = lessons;
                        groupInfo.Number = int.Parse(group);
                        groupInfos.Add(groupInfo);
                        continue;
                    }

                    var lessonNumbers = lessonsElements[0].FindElements(By.XPath(".//th")).ToList();
                    var lessonNames = lessonsElements[1].FindElements(By.XPath(".//td")).ToList();
                    var lessonCabinets = lessonsElements[2].FindElements(By.XPath(".//td")).ToList();

                    for (int j = 0; j < lessonNumbers.Count; j++)
                    {
                        string cabinet = lessonCabinets.Count < lessonNumbers.Count && lessonCabinets.Count <= j
                            ? "-"
                            : lessonCabinets[j].Text;

                        lessons.Add(new Lesson()
                        {
                            Number = int.Parse(lessonNumbers[j].Text.Replace("№", "")),
                            Cabinet = cabinet,
                            Group = group,
                            Name = lessonNames[j].Text
                        });
                    }

                    groupInfo.Number = int.Parse(group);
                    groupInfo.Lessons = lessons;
                    groupInfos.Add(groupInfo);
                    break;
                }
            }
            catch (Exception e)
            {
                if (this._config.Entries.Administrators is not { } administrators) continue;
                var adminTelegramId = administrators.FirstOrDefault();
                if (adminTelegramId == default) continue;

                this._botService.SendMessage(new SendMessageArgs(adminTelegramId, e.Message));
                this._botService.SendMessage(new SendMessageArgs(adminTelegramId,
                    "Ошибка дневного расписания в группе: " + group));
            }
        }

        driver.Dispose();

        foreach (var groupInfo in groupInfos)
        {
            int count = 0;
            groupInfo.Lessons.Reverse();
            foreach (var lesson in groupInfo.Lessons)
            {
                if (lesson.Name.Length < 1) count++;
                else break;
            }

            groupInfo.Lessons.RemoveRange(0, count);
            groupInfo.Lessons.Reverse();

            if (groupInfo.Lessons.Count < 1) continue;

            for (int i = 0; i < groupInfo.Lessons.First().Number - 1; i++)
            {
                groupInfo.Lessons.Add(new Lesson()
                {
                    Cabinet = "-",
                    Group = groupInfo.Number.ToString(),
                    Name = "-",
                    Number = i + 1
                });
            }

            groupInfo.Lessons = groupInfo.Lessons.OrderBy(l => l.Number).ToList();
        }

        this.TempTimetable.Add(new Day()
        {
            GroupInfos = new List<GroupInfo>(groupInfos)
        });
        groupInfos.Clear();
        await this.ValidateTimetableHashes();
        this._dayParseStarted = false;
    }

    private async Task ValidateTimetableHashes()
    {
        if (this.TempTimetable.Any(e => e.GroupInfos.Count == 0)) return;
        var tempTimetable = new List<Day>(this.TempTimetable);
        this.TempTimetable.Clear();
        
        if (tempTimetable.Count > this.Timetable.Count)
        {
            this.Timetable.Clear();
            this.Timetable = new List<Day>(tempTimetable);
            await this.SendNewDayTimetables(null, true);
            tempTimetable.Clear();
            return;
        }

        for (var i = 0; i < tempTimetable.Count; i++)
        {
            var tempDay = tempTimetable[i];
            var day = this.Timetable[i];

            for (int j = 0; j < tempDay.GroupInfos.Count; j++)
            {
                var tempGroup = tempDay.GroupInfos[j].Number;

                var tempLessons = tempDay.GroupInfos[j].Lessons;
                var groupInfo = day.GroupInfos.FirstOrDefault(g => g.Number == tempDay.GroupInfos[j].Number);

                if (groupInfo == default || tempLessons.Count != groupInfo.Lessons.Count)
                {
                    _ = this.SendNewDayTimetables(tempGroup.ToString());
                    continue;
                }

                for (var h = 0; h < tempLessons.Count; h++)
                {
                    var tempLesson = tempLessons[h];
                    var lesson = groupInfo.Lessons[h];

                    if (tempLesson.GetHashCode() == lesson.GetHashCode()) continue;
                    _ = this.SendNewDayTimetables(tempGroup.ToString());
                    break;
                }
            }
        }

        this.Timetable.Clear();
        this.Timetable = new List<Day>(tempTimetable);
        tempTimetable.Clear();
    }

    private async Task SendNewDayTimetables(string? group, bool all = false)
    {
        Console.WriteLine("Изменение дневного расписания для: " + (all ? "Всех" : group));
        
        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var users = (await userCollection.FindAsync(u => all || u.Group == group)).ToList();

        foreach (var user in users.Where(user => user.Notifications && user.Group is not null))
        {
            if (this.Timetable.Count < 1)
            {
                this._botService.SendMessage(new SendMessageArgs(user.UserId, $"У {user.Group} группы нет пар"));
                continue;
            }

            var tasks = new List<Task>();

            foreach (var day in this.Timetable)
            {
                var message = day.Date + "\n";

                foreach (var groupInfo in day.GroupInfos.Where(groupInfo =>
                             user.Group != null && int.Parse(user.Group) == groupInfo.Number))
                {
                    if (groupInfo.Lessons.Count < 1)
                    {
                        message = $"У {groupInfo.Number} группы нет пар";
                        continue;
                    }

                    message = this.CreateDayTimetableMessage(groupInfo);
                }

                tasks.Add(this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    message.Length < 1 ? $"У вашей группы нет пар" : message)
                {
                    ParseMode = ParseMode.Markdown
                }));
            }

            await Task.WhenAll(tasks);
        }
    }

    public async Task SendDayTimetable(User telegramUser)
    {
        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
        if (user is null) return;

        if (user.Group is null)
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        if (this.Timetable.Count < 1)
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, $"У {user.Group} группы нет пар"));
            return;
        }

        foreach (var day in this.Timetable)
        {
            string message = day.Date + "\n";

            foreach (var groupInfo in day.GroupInfos.Where(groupInfo => int.Parse(user.Group) == groupInfo.Number))
            {
                if (groupInfo.Lessons.Count < 1)
                {
                    message = $"У {groupInfo.Number} группы нет пар";
                    continue;
                }

                message = this.CreateDayTimetableMessage(groupInfo);
            }

            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                message.Length < 1 ? $"У вашей группы нет пар" : message)
            {
                ParseMode = ParseMode.Markdown
            });
        }
    }

    private string CreateDayTimetableMessage(GroupInfo groupInfo)
    {
        string message = string.Empty;

        message += $"Группа: *{groupInfo.Number}*\n\n";

        foreach (var lesson in groupInfo.Lessons)
        {
            var lessonName = Utils.HtmlTagsFix(lesson.Name).Replace('\n', ' ');
            var cabinet = Utils.HtmlTagsFix(lesson.Cabinet).Replace('\n', ' ');
            var newlineIndexes = new List<int>();
            for (int i = 0; i < lessonName.Length; i++)
            {
                if (int.TryParse(lessonName[i].ToString(), out _) && i != 0)
                {
                    newlineIndexes.Add(i);
                }
            }

            if (newlineIndexes.Count > 0)
            {
                foreach (var newlineIndex in newlineIndexes)
                {
                    lessonName = lessonName.Insert(newlineIndex, "\n");
                }
            }

            message +=
                $"*Пара: №{lesson.Number}*" +
                $"\n{(lessonName.Length < 2 ? "Предмет: -" : $"{lessonName}")}" +
                $"\n{(cabinet.Length < 2 ? "Каб: -" : $"Каб: {cabinet}")}" +
                $"\n\n";
        }

        return message;
    }

    public async Task ParseWeekTimetables()
    {
        if (this._weekParseStarted) return;
        this._weekParseStarted = true;

        var web = new HtmlWeb();
        var doc = web.Load(WeekUrl);

        var content = doc.DocumentNode.SelectNodes("//div/div/div/div/div/div").FirstOrDefault();
        if (content != default) this.LastWeekHtmlContent = content.InnerText;

        var students = doc.DocumentNode.SelectNodes("//h2");
        if (students is null)
        {
            this._weekParseStarted = false;
            return;
        }

        var newDate = doc.DocumentNode.SelectNodes("//h3")[0].InnerText.Trim();
        var dateDbCollection = this._mongoService.Database.GetCollection<Timetable>("WeekTimetables");
        var dbTables = (await dateDbCollection.FindAsync(d => true)).ToList();
        
        var driver = Utils.CreateChromeDriver();

        foreach (var group in this.Groups)
        {
            try
            {
                var filePath = $"./photo/Группа - {group}.png";

                driver.Navigate().GoToUrl($"{WeekUrl}?group={group}");

                Utils.ModifyUnnecessaryElementsOnWebsite(ref driver);

                var element = driver.FindElements(By.TagName("h2")).FirstOrDefault();
                if (element == default) continue;

                var actions = new Actions(driver);
                actions.MoveToElement(element).Perform();

                var screenshot = (driver as ITakesScreenshot).GetScreenshot();
                screenshot.SaveAsFile(filePath, ScreenshotImageFormat.Png);

                var image = await Image.LoadAsync(filePath);

                image.Mutate(x => x.Resize((int)(image.Width / 1.5), (int)(image.Height / 1.5)));
                await image.SaveAsPngAsync(filePath);
            }
            catch (Exception e)
            {
                if (this._config.Entries.Administrators is not { } administrators) continue;
                var adminTelegramId = administrators.FirstOrDefault();
                if (adminTelegramId == default) continue;

                this._botService.SendMessage(new SendMessageArgs(adminTelegramId, e.Message));
                this._botService.SendMessage(new SendMessageArgs(adminTelegramId, "Ошибка в группе: " + group));
            }
        }

        driver.Dispose();

        if (!dbTables.Exists(table => table.Date.Trim() == newDate))
        {
            await dateDbCollection.InsertOneAsync(new Timetable()
            {
                Date = newDate
            });
        }

        this._weekParseStarted = false;
        //await this.SendNotificationsAboutWeekTimetable();
    }

    public async Task SendWeekTimetable(User telegramUser)
    {
        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
        if (user is null) return;

        if (user.Group is null)
        {
            this._botService.SendMessage(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        Image? image;
        try
        {
            image = await Image.LoadAsync($"./photo/Группа - {user.Group}.png");
        }
        catch
        {
            this._botService.SendMessage(new SendMessageArgs(user.UserId, "Увы, данная группа не найдена"));
            return;
        }

        using (var ms = new MemoryStream())
        {
            await image.SaveAsync(ms, new PngEncoder());

            this._botService.SendPhoto(new SendPhotoArgs(user.UserId,
                new InputFile(ms.ToArray(), $"./photo/Группа - {user.Group}.png")));
        }
    }

    public async Task SendNotificationsAboutWeekTimetable()
    {
        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var users = (await userCollection.FindAsync(u => true)).ToList();
        if (users is null) return;

        var tasks = new List<Task>();

        foreach (var user in users)
        {
            if (user.Group is null || !user.Notifications) continue;

            tasks.Add(this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                "Колледж обновил страницу расписания на неделю")));
        }

        await Task.WhenAll(tasks);
    }

    private Task NewDayTimetableCheck()
    {
        var driver = Utils.CreateChromeDriver();
        driver.Manage().Timeouts().PageLoad = new TimeSpan(0, 0, 20);

        driver.Navigate().GoToUrl(DayUrl);

        var content = driver.FindElement(By.Id("wrapperTables")).Text;

        driver.Dispose();

       if (this.LastDayHtmlContent == content) return Task.CompletedTask;

        _ = this.ParseDayTimetables().ContinueWith((t) => { Console.WriteLine(t.Exception?.InnerException); },
            TaskContinuationOptions.OnlyOnFaulted);

        return Task.FromResult(Task.CompletedTask);
    }

    private Task NewWeekTimetableCheck()
    {
        var web = new HtmlWeb();
        var doc = web.Load(WeekUrl);
        var content = doc.DocumentNode.SelectNodes("//div/div/div/div/div/div").FirstOrDefault();
        if (content == default) return Task.CompletedTask;

        if (this.LastWeekHtmlContent == content.InnerText) return Task.CompletedTask;

        _ = this.ParseWeekTimetables().ContinueWith((t) => { Console.WriteLine(t.Exception?.InnerException); },
            TaskContinuationOptions.OnlyOnFaulted);

        return Task.CompletedTask;
    }
}