using HtmlAgilityPack;
using MongoDB.Driver;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using SixLabors.ImageSharp.Formats.Png;
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
    Task ParseDayTimetables(bool firstStart = false);
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

    private static string LastDayHtmlContent { get; set; }
    private static string LastWeekHtmlContent { get; set; }

    public List<string> Groups { get; set; } = new()
    { 
        "8", "41", "42", "43", "44", "45", "46", "48", "49", "50", "51",
        "52", "53", "54", "55", "56", "57", "58", "60", "61", "62", "63", "64", "65",
        "66", "67", "68", "69", "70", "71", "72", "73", "74", "75",
        "76", "77", "78"
    };

    private static List<Day> _tempTimetable { get; set; } = new();
    private static List<Day> _timetable { get; set; } = new();
    private static Dictionary<string, Image> _images = new();

    public ParserService(IMongoService mongoService, IBotService botService, IConfig<MainConfig> config)
    {
        _mongoService = mongoService;
        _botService = botService;
        _config = config;

        var parseTimer = new Timer(1_000_000)
        {
            AutoReset = true, Enabled = true
        };
        parseTimer.Elapsed += async (sender, args) =>
        {
            try
            {
                await NewTimetableCheck();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        };
    }

    public async Task ParseDayTimetables(bool firstStart = false)
    {
        Console.WriteLine("Запущено дневное расписание");

        var driver = ChromeService.Driver;
        driver.Navigate().GoToUrl(DayUrl);

        var content = driver.FindElement(By.Id("wrapperTables"));

        if (content is null)
        {
            return;
        }

        LastDayHtmlContent = content.Text;
        List<GroupInfo> groupInfos = new List<GroupInfo>();
        _tempTimetable.Clear();

        var groupsAndLessons = content.FindElements(By.XPath(".//div")).ToList();

        foreach (var group in Groups)
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
                if (_config.Entries.Administrators is not { } administrators) continue;
                var adminTelegramId = administrators.FirstOrDefault();
                if (adminTelegramId == default) continue;

                _botService.SendMessage(new SendMessageArgs(adminTelegramId, e.Message));
                _botService.SendMessage(new SendMessageArgs(adminTelegramId,
                    "Ошибка дневного расписания в группе: " + group));
            }
        }

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

        // _tempTimetable.Add(new Day()
        // {
        //     GroupInfos = new List<GroupInfo>(groupInfos)
        // });
        _timetable.Clear();
        _timetable.Add(new Day()
        {
            GroupInfos = new List<GroupInfo>(groupInfos)
        });
        groupInfos.Clear();
        //await ValidateTimetableHashes(firstStart);
        Console.WriteLine("Завершено дневное расписание");
    }

    private async Task ValidateTimetableHashes(bool firstStart)
    {
        if (_tempTimetable.Any(e => e.GroupInfos.Count == 0)) return;
        
        if (_tempTimetable.Count > _timetable.Count)
        {
            _timetable.Clear();
            _timetable = new List<Day>(_tempTimetable);
            await SendNewDayTimetables(null, firstStart, true);
            _tempTimetable.Clear();
            return;
        }

        List<string> changedGroups = new();
        for (var i = 0; i < _tempTimetable.Count; i++)
        {
            var tempDay = _tempTimetable[i];
            var day = _timetable[i];

            for (int j = 0; j < tempDay.GroupInfos.Count; j++)
            {
                var tempGroup = tempDay.GroupInfos[j].Number;

                var tempLessons = tempDay.GroupInfos[j].Lessons;
                var groupInfo = day.GroupInfos.FirstOrDefault(g => g.Number == tempDay.GroupInfos[j].Number);

                if (groupInfo == default || tempLessons.Count != groupInfo.Lessons.Count)
                {
                    changedGroups.Add(tempGroup.ToString());
                    continue;
                }

                for (var h = 0; h < tempLessons.Count; h++)
                {
                    var tempLesson = tempLessons[h];
                    var lesson = groupInfo.Lessons[h];

                    if (tempLesson.GetHashCode() == lesson.GetHashCode()) continue;
                    changedGroups.Add(tempGroup.ToString());
                    break;
                }
            }
        }
        
        _timetable.Clear();
        _timetable = new List<Day>(_tempTimetable);
        _tempTimetable.Clear();
        
        changedGroups.ForEach(group =>
        {
            _ = SendNewDayTimetables(group.ToString(), firstStart);
        });
    }

    private async Task SendNewDayTimetables(string? group, bool firstStart, bool all = false)
    {
        if (firstStart) return;

        if (_config.Entries.Administrators is { } administrators)
        {
            var adminTelegramId = administrators.FirstOrDefault();
            if (adminTelegramId != default)
            {
                _botService.SendMessage(new SendMessageArgs(adminTelegramId,
                    "Изменение дневного расписания учеников для: " + (all ? "Всех" : group)));
            }
        }

        var userCollection = _mongoService.Database.GetCollection<Models.User>("Users");
        var users = (await userCollection.FindAsync(u => all || u.Group == group)).ToList();

        foreach (var user in users.Where(user => user.Notifications && user.Group is not null))
        {
            if (_timetable.Count < 1)
            {
                _botService.SendMessage(new SendMessageArgs(user.UserId, $"У {user.Group} группы нет пар"));
                continue;
            }

            var tasks = new List<Task>();

            foreach (var day in _timetable)
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

                    message = CreateDayTimetableMessage(groupInfo);
                }

                tasks.Add(_botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                    message.Trim().Length <= 1 ? $"У вашей группы нет пар" : message)
                {
                    ParseMode = ParseMode.Markdown
                }));
            }

            await Task.WhenAll(tasks);
        }
    }

    public async Task SendDayTimetable(User telegramUser)
    {
        var userCollection = _mongoService.Database.GetCollection<Models.User>("Users");
        var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
        if (user is null) return;

        if (user.Group is null)
        {
            await _botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        if (_timetable.Count < 1)
        {
            await _botService.SendMessageAsync(new SendMessageArgs(user.UserId, $"У {user.Group} группы нет пар"));
            return;
        }

        foreach (var day in _timetable)
        {
            string message = day.Date + "\n";

            foreach (var groupInfo in day.GroupInfos.Where(groupInfo => int.Parse(user.Group) == groupInfo.Number))
            {
                if (groupInfo.Lessons.Count < 1)
                {
                    message = $"У {groupInfo.Number} группы нет пар";
                    continue;
                }

                message = CreateDayTimetableMessage(groupInfo);
            }

            await _botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                message.Trim().Length <= 1 ? $"У вашей группы нет пар" : message)
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
        Console.WriteLine("Запущено недельное расписание");

        var web = new HtmlWeb();
        var doc = web.Load(WeekUrl);

        var content = doc.DocumentNode.SelectNodes("//div/div/div/div/div/div").FirstOrDefault();
        if (content != default) LastWeekHtmlContent = content.InnerText;

        var students = doc.DocumentNode.SelectNodes("//h2");
        if (students is null)
        {
            return;
        }

        // var newDate = doc.DocumentNode.SelectNodes("//h3")[0].InnerText.Trim();
        // var dateDbCollection = _mongoService.Database.GetCollection<Timetable>("WeekTimetables");
        // var dbTables = (await dateDbCollection.FindAsync(d => true)).ToList();

        var driver = ChromeService.Driver;

        foreach (var group in Groups)
        {
            try
            {
                driver.Navigate().GoToUrl($"{WeekUrl}?group={group}");

                Utils.ModifyUnnecessaryElementsOnWebsite(ref driver);

                var element = driver.FindElement(By.TagName("h2"));
                if (element == default) continue;

                var actions = new Actions(driver);
                actions.MoveToElement(element).Perform();

                var screenshot = (driver as ITakesScreenshot).GetScreenshot();
                if (_images.TryGetValue(group, out var oldImage))
                {
                    oldImage.Dispose();
                    _images.Remove(group);
                }

                var image = Image.Load(screenshot.AsByteArray);
                image.Mutate(x => x.Resize((int)(image.Width / 1.5), (int)(image.Height / 1.5)));

                _images.Add(group, image);
            }
            catch (Exception e)
            {
                if (_config.Entries.Administrators is not { } administrators) continue;
                var adminTelegramId = administrators.FirstOrDefault();
                if (adminTelegramId == default) continue;

                _botService.SendMessage(new SendMessageArgs(adminTelegramId, e.Message));
                _botService.SendMessage(new SendMessageArgs(adminTelegramId, "Ошибка в группе: " + group));
            }
        }
        

        // if (!dbTables.Exists(table => table.Date.Trim() == newDate))
        // {
        //     await dateDbCollection.InsertOneAsync(new Timetable()
        //     {
        //         Date = newDate
        //     });
        // }
        
        Console.WriteLine("Завершено недельное расписание");
        //await SendNotificationsAboutWeekTimetable();
    }

    public async Task SendWeekTimetable(User telegramUser)
    {
        var userCollection = _mongoService.Database.GetCollection<Models.User>("Users");
        var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
        if (user is null) return;

        if (user.Group is null)
        {
            _botService.SendMessage(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        _images.TryGetValue(user.Group, out var image);

        if (image is null)
        {
            _botService.SendMessage(new SendMessageArgs(user.UserId, "Увы, данная группа не найдена"));
            return;
        }

        using (var ms = new MemoryStream())
        {
            await image.SaveAsync(ms, new PngEncoder());

            _botService.SendPhoto(new SendPhotoArgs(user.UserId,
                new InputFile(ms.ToArray(), $"Group - {user.Group}")));

            await ms.DisposeAsync();
        }
    }

    public async Task SendNotificationsAboutWeekTimetable()
    {
        var userCollection = _mongoService.Database.GetCollection<Models.User>("Users");
        var users = (await userCollection.FindAsync(u => true)).ToList();
        if (users is null) return;

        var tasks = new List<Task>();

        foreach (var user in users)
        {
            if (user.Group is null || !user.Notifications) continue;

            tasks.Add(_botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                "Колледж обновил страницу расписания на неделю")));
        }

        await Task.WhenAll(tasks);
    }

    private async Task NewTimetableCheck()
    {
        bool parseDay = false, parseWeek = false;
        var driver = ChromeService.Driver;

        //Day
        driver.Navigate().GoToUrl(DayUrl);
        var contentElement = driver.FindElement(By.Id("wrapperTables"));
        bool emptyContent = driver.FindElements(By.XPath(".//div")).ToList().Count < 5;


        if (!emptyContent && LastDayHtmlContent != contentElement.Text)
        {
            parseDay = true;
        }

        driver.Navigate().GoToUrl(WeekUrl);

        var content = driver.FindElement(By.ClassName("entry")).Text;

        if (content != default && LastWeekHtmlContent != content)
        {
            parseWeek = true;
        }

        try
        {
            if (parseDay) await this.ParseDayTimetables();
            if (parseWeek) await this.ParseWeekTimetables();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}