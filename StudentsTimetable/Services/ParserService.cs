using MongoDB.Driver;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
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
    Task ParseWeek();
    Task ParseDay();
    Task SendWeek(User telegramUser);
    Task SendDayTimetable(User telegramUser);
    Task UpdateTimetableTick();
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
        "8", "49", "50", "51", "52", "53", "54", "55", "56", "57", "58",
        "60", "61", "62", "63", "64", "65", "66", "67", "68", "69", "70", 
        "71", "72", "73", "74", "75", "76", "77", "78", "79", "80", "81", 
        "82", "83", "84"
    };

    private static List<Day> TempTimetable { get; set; } = new();
    private static List<Day> Timetable { get; set; } = new();

    public ParserService(IMongoService mongoService, IBotService botService, IConfig<MainConfig> config)
    {
        this._mongoService = mongoService;
        this._botService = botService;
        this._config = config;

        var parseTimer = new Timer(1_000_000)
        {
            AutoReset = true, Enabled = true
        };
        parseTimer.Elapsed += async (sender, args) =>
        {
            try
            {
                await this.UpdateTimetableTick();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        };
    }

    public Task ParseDay()
    {
        Console.WriteLine("Start parse day");
        
        var groupInfos = new List<GroupInfo>();
        var driver = ChromeService.Driver;

        driver.Navigate().GoToUrl(DayUrl);

        var content = driver.FindElement(By.Id("wrapperTables"));

        if (content is null) return Task.CompletedTask;
        
        LastDayHtmlContent = content.Text;
        TempTimetable.Clear();
        var groupsAndLessons = content.FindElements(By.XPath(".//div")).ToList();

        foreach (var group in this.Groups)
        {
            try
            {
                for (var i = 1; i < groupsAndLessons.Count; i += 2)
                {
                    if (groupsAndLessons[i - 1].Text.Split('-')[0].Trim() != group) continue;
                    var groupInfo = new GroupInfo();
                    var lessons = new List<Lesson>();

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
                
                //driver.Close();
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

       
        Timetable.Clear();
        Timetable.Add(new Day
        {
            GroupInfos = new List<GroupInfo>(groupInfos)
        });
        groupInfos.Clear();
        //driver.Close();
        Console.WriteLine("End parse day");
        return Task.CompletedTask;
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

        if (Timetable.Count < 1)
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, $"У {user.Group} группы нет пар"));
            return;
        }

        foreach (var day in Timetable)
        {
            string message = day.Date + "\n";

            foreach (var groupInfo in day.GroupInfos.Where(groupInfo => int.Parse(user.Group) == groupInfo.Number))
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

    public Task ParseWeek()
    {
        Console.WriteLine("Start parse week");

        var driver = ChromeService.Driver;
        
        driver.Navigate().GoToUrl(WeekUrl);
        
        var content = driver.FindElements(By.XPath("//div/div/div/div/div/div")).FirstOrDefault();
        if (content != default)
        {
            LastWeekHtmlContent = content.Text;
            //driver.Close();
        }

        foreach (var group in this.Groups)
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
                var image = Image.Load(screenshot.AsByteArray);
                
                image.Mutate(x => x.Resize((int) (image.Width / 1.5), (int) (image.Height / 1.5)));

                image.SaveAsync($"./cachedImages/{group}.png");
                //driver.Close();
            }
            catch (Exception e)
            {
                if (this._config.Entries.Administrators is not { } administrators) continue;
                var adminTelegramId = administrators.FirstOrDefault();
                if (adminTelegramId == default) continue;

                this._botService.SendMessage(new SendMessageArgs(adminTelegramId, e.Message));
                this._botService.SendMessage(new SendMessageArgs(adminTelegramId, "Ошибка в группе: " + group));
                //driver.Close();
            }
        }

        Console.WriteLine("End week parse");
        return Task.CompletedTask;
    }

    public async Task SendWeek(User telegramUser)
    {
        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
        if (user is null) return;

        if (user.Group is null)
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        var image = await Image.LoadAsync($"./cachedImages/{user.Group}.png");

        if (image is not {})
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Увы, данная группа не найдена"));
            return;
        }

        using (var ms = new MemoryStream())
        {
            await image.SaveAsync(ms, new PngEncoder());

            await this._botService.SendPhotoAsync(new SendPhotoArgs(user.UserId,new InputFile(ms.ToArray(), $"Group - {user.Group}")));

            await ms.DisposeAsync();
            image.Dispose();
        }
    }

    public async Task UpdateTimetableTick()
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

        //driver.Close();
        driver.Navigate().GoToUrl(WeekUrl);

        var content = driver.FindElement(By.ClassName("entry")).Text;

        if (content != default && LastWeekHtmlContent != content)
        {
            parseWeek = true;
        }
        
        //driver.Close();

        try
        {
            if (parseWeek) await this.ParseWeek();
            if (parseDay) await this.ParseDay();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}