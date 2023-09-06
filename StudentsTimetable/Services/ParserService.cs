using System.Drawing;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using StudentsTimetable.Models;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableMethods.FormattingOptions;
using Telegram.BotAPI.AvailableTypes;
using TelegramBot_Timetable_Core.Services;
using Exception = System.Exception;
using File = System.IO.File;
using Image = SixLabors.ImageSharp.Image;
using Size = System.Drawing.Size;
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
    private readonly IChromeService _chromeService;

    private const string WeekUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-неделю";
    private const string DayUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-день";
    private const int DriverTimeout = 200;

    private static string LastDayHtmlContent { get; set; }
    private static string LastWeekHtmlContent { get; set; }

    public List<string> Groups { get; set; } = new()
    {
        "8", "49", "50", "51", "52", "53", "54", "55", "56", "57", "58", "59*",
        "60", "61", "62", "63", "64", "65", "66", "67", "68", "69", "70",
        "71", "72", "73", "74", "75", "76", "77", "78", "79", "80", "81",
        "82", "83", "84", "160*", "162*", "163*", "164*", "165*", "166*"
    };

    private static List<Day> Timetable { get; set; } = new();

    public ParserService(IMongoService mongoService, IBotService botService, IChromeService chromeService)
    {
        this._mongoService = mongoService;
        this._botService = botService;
        this._chromeService = chromeService;

        if (!Directory.Exists("./cachedImages")) Directory.CreateDirectory("./cachedImages");

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
        var (service, options, delay) = this._chromeService.Create();
        var group = string.Empty;
        using (FirefoxDriver driver = new FirefoxDriver(service, options, delay))
        {
            driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            driver.Navigate().GoToUrl(DayUrl);
            Thread.Sleep(2000);

            var content = driver.FindElement(By.Id("wrapperTables"));
            wait.Until(d => content.Displayed);
            if (content is null) return Task.CompletedTask;

            var groupsAndLessons = content.FindElements(By.XPath(".//div")).ToList();

            try
            {
                for (var i = 1; i < groupsAndLessons.Count; i += 2)
                {
                    var parsedGroupName = groupsAndLessons[i - 1].Text.Split('-')[0].Trim();
                    group = this.Groups.FirstOrDefault(g => g == parsedGroupName); 
                    if (group is null) continue;
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

                    groupInfo.Number = int.Parse(group.Replace("*", ""));
                    groupInfo.Lessons = lessons;
                    groupInfos.Add(groupInfo);
                }
            }
            catch (Exception e)
            {
                this._botService.SendAdminMessage(new SendMessageArgs(0, e.Message));
                this._botService.SendAdminMessage(new SendMessageArgs(0,
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
            var groupInfoFromTimetable = Timetable.LastOrDefault()?.GroupInfos.FirstOrDefault(g=>g.Number == groupInfo.Number);
            if(groupInfoFromTimetable is null || groupInfoFromTimetable.Equals(groupInfo)) continue;
            this._botService.SendAdminMessageAsync(new SendMessageArgs(0, $"Расписание у группы {groupInfo.Number}"));
        }


        Timetable.Clear();
        Timetable.Add(new Day
        {
            GroupInfos = new List<GroupInfo>(groupInfos)
        });
        groupInfos.Clear();

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

    public Task ParseWeek()
    {
        Console.WriteLine("Start parse week");

        var (service, options, delay) = this._chromeService.Create();
        using (FirefoxDriver driver = new FirefoxDriver(service, options, delay))
        {
            driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(120));

            driver.Navigate().GoToUrl(WeekUrl);
            //Thread.Sleep(DriverTimeout);
            var element = driver.FindElement(By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div"));
            wait.Until(d => element.Displayed);
            Utils.ModifyUnnecessaryElementsOnWebsite(driver);

            if (element == default) return Task.CompletedTask;
            var h2 =
                driver.FindElements(
                    By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div/h2"));

            var h3 =
                driver.FindElements(
                    By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div/h3"));
            var table = driver.FindElements(By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div/div"));
            Utils.HideGroupElements(driver, h3);
            Utils.HideGroupElements(driver, h2);
            Utils.HideGroupElements(driver, table);
            for (var i = 0; i < h2.Count; i++)
            {
                var groupH2 = h2[i];
                var groupName = string.Empty;
                var list = new List<IWebElement> { groupH2, h3[i], table[i] };
                try
                {
                    //Thread.Sleep(DriverTimeout - 1850);
                    var actions = new Actions(driver);
                    Utils.ShowGroupElements(driver, list);
                    actions.MoveToElement(groupH2).Perform();
                    groupName = this.Groups.First(g => g == groupH2.Text.Split('-')[1].Trim());
                    driver.Manage().Window.Size =
                        new Size(1920, driver.FindElement(By.ClassName("main")).Size.Height - 30);
                    var screenshot = (driver as ITakesScreenshot).GetScreenshot();
                    using var image = Image.Load(screenshot.AsByteArray);
                    image.Mutate(x => x.Resize((int)(image.Width / 1.5), (int)(image.Height / 1.5)));
                    image.SaveAsync($"./cachedImages/{groupName.Replace("*", "knor")}.png");
                }
                catch (Exception e)
                {
                    this._botService.SendAdminMessage(new SendMessageArgs(0, e.Message));
                    this._botService.SendAdminMessage(new SendMessageArgs(0, "Ошибка в группе: " + groupName));
                }
                finally
                {
                    Utils.HideGroupElements(driver, list);
                }
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

        if (user.Group is null || !File.Exists($"./cachedImages/{user.Group.Replace("*", "knor")}.png"))
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Вы еще не выбрали группу"));
            return;
        }

        var image = await Image.LoadAsync($"./cachedImages/{user.Group.Replace("*", "knor")}.png");

        if (image is not { })
        {
            await this._botService.SendMessageAsync(new SendMessageArgs(user.UserId, "Увы, данная группа не найдена"));
            return;
        }

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);

        await this._botService.SendPhotoAsync(new SendPhotoArgs(user.UserId,
            new InputFile(ms.ToArray(), $"Group - {user.Group}")));
    }

    public async Task UpdateTimetableTick()
    {
        Console.WriteLine("Start update tick");
        try
        {
            bool parseDay = false, parseWeek = false;
            var (service, options, delay) = this._chromeService.Create();
            using (FirefoxDriver driver = new FirefoxDriver(service, options, delay))
            {
                //Day
                driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                driver.Navigate().GoToUrl(DayUrl);
                Thread.Sleep(DriverTimeout);

                var contentElement = driver.FindElement(By.Id("wrapperTables"));
                wait.Until(d => contentElement.Displayed);
                bool emptyContent = driver.FindElements(By.XPath(".//div")).ToList().Count < 5;
                
                if (!emptyContent && LastDayHtmlContent != contentElement.Text)
                {
                    parseDay = true;
                    LastDayHtmlContent = contentElement.Text;
                }

                driver.Navigate().GoToUrl(WeekUrl);
                Thread.Sleep(DriverTimeout);
                
                var content = driver.FindElement(By.ClassName("entry"));
                wait.Until(d => content.Displayed);
                
                if (content != default && LastWeekHtmlContent != content.Text)
                {
                    parseWeek = true;
                    LastWeekHtmlContent = content.Text;
                }
            }

            if (parseWeek)
            {
                await this._botService.SendAdminMessageAsync(new SendMessageArgs(0, "Start parse week"));
                await this.ParseWeek();
                await this._botService.SendAdminMessageAsync(new SendMessageArgs(0, "End parse week"));
            }

            if (parseDay)
            {
                await this._botService.SendAdminMessageAsync(new SendMessageArgs(0, "Start parse day"));
                await this.ParseDay();
                await this._botService.SendAdminMessageAsync(new SendMessageArgs(0, "End parse day"));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        Console.WriteLine("End update tick");
    }
}