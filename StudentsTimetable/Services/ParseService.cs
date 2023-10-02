using System.Text.RegularExpressions;
using MongoDB.Driver;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using StudentsTimetable.Config;
using StudentsTimetable.Models;
using Telegram.BotAPI.AvailableMethods;
using TelegramBot_Timetable_Core.Config;
using TelegramBot_Timetable_Core.Services;
using Size = System.Drawing.Size;
using Timer = System.Timers.Timer;

namespace StudentsTimetable.Services;

public interface IParseService
{
    string[] Groups { get; init; }
    static List<Day> Timetable { get; set; }
    Task UpdateTimetableTick();
}

public class ParseService : IParseService
{
    private readonly IMongoService _mongoService;
    private readonly IBotService _botService;
    private readonly IFirefoxService _firefoxService;
    private readonly IDistributionService _distributionService;
    private string _weekInterval;
    private const string WeekUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-неделю";
    private const string DayUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-день";

    private static string LastDayHtmlContent { get; set; }
    private static string LastWeekHtmlContent { get; set; }

    private const int DriverTimeout = 2000;

    public string[] Groups { get; init; }

    public static List<Day> Timetable { get; set; } = new();

    public ParseService(IMongoService mongoService, IBotService botService, IFirefoxService firefoxService,
        IDistributionService distributionService, IConfig<GroupsConfig> groups)
    {
        this._mongoService = mongoService;
        this._botService = botService;
        this._firefoxService = firefoxService;
        this._distributionService = distributionService;
        this.Groups = groups.Entries.Groups;
        if (!Directory.Exists("./cachedImages")) Directory.CreateDirectory("./cachedImages");

        var parseTimer = new Timer(1_000_000)
        {
            AutoReset = true, Enabled = true
        };
        parseTimer.Elapsed += async (_, _) =>
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

    private async Task ParseDay()
    {
        Console.WriteLine("Start parse day");

        var groupInfos = new List<GroupInfo>();
        var (service, options, delay) = this._firefoxService.Create();
        var group = string.Empty;
        var day = string.Empty;
        using (FirefoxDriver driver = new FirefoxDriver(service, options, delay))
        {
            driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            driver.Navigate().GoToUrl(DayUrl);
            Thread.Sleep(DriverTimeout);
            var content = driver.FindElement(By.XPath("//*[@id=\"wrapperTables\"]"));
            wait.Until(d => content.Displayed);
            if (content is null) return;

            var groupsAndLessons = content.FindElements(By.XPath(".//div")).ToList();
            if (groupsAndLessons.Count > 0) day = groupsAndLessons[0].Text.Split('-')[1].Trim();
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
                        var cabinet = lessonCabinets.Count < lessonNumbers.Count && lessonCabinets.Count <= j
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
                _ = this._botService.SendAdminMessageAsync(new SendMessageArgs(0, e.Message));
                _ = this._botService.SendAdminMessageAsync(new SendMessageArgs(0,
                    "Ошибка дневного расписания в группе: " + group));
            }
        }

        var notificationUserList = new List<User>();
        var groupUpdatedList = new List<int>();
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

            var groupInfoFromTimetable =
                Timetable.LastOrDefault()?.GroupInfos.FirstOrDefault(g => g.Number == groupInfo.Number);
            if (groupInfo.Lessons.Count < 1)
            {
                if (groupInfoFromTimetable?.Lessons is not null && groupInfoFromTimetable.Lessons.Count > 0)
                    notificationUserList.AddRange(
                        (await this._mongoService.Database.GetCollection<User>("Users")
                            .FindAsync(u => u.Group != null && u.Notifications)).ToList().Where(u =>
                        {
                            if (u?.Group != null &&
                                int.TryParse(Regex.Replace(u.Group, "[^0-9]", ""), out int userGroupNumber))
                                return userGroupNumber == groupInfo.Number;
                            return false;
                        }).ToList());
                continue;
            }

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

            if (groupInfoFromTimetable is null || groupInfoFromTimetable.Equals(groupInfo)) continue;

            groupUpdatedList.Add(groupInfo.Number);
            try
            {
                notificationUserList.AddRange(
                    (await this._mongoService.Database.GetCollection<User>("Users")
                        .FindAsync(u => u.Group != null && u.Notifications)).ToList().Where(u =>
                    {
                        if (u?.Group != null &&
                            int.TryParse(Regex.Replace(u.Group, "[^0-9]", ""), out int userGroupNumber))
                            return userGroupNumber == groupInfo.Number;
                        return false;
                    }).ToList());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        if (groupUpdatedList.Count != 0)
            _ = this._botService.SendAdminMessageAsync(new SendMessageArgs(0,
                $"There's been a schedule change with the groups: {string.Join(',', groupUpdatedList)}"));
        Timetable.Clear();
        Timetable.Add(new Day
        {
            Date = day,
            GroupInfos = new List<GroupInfo>(groupInfos)
        });
        groupInfos.Clear();

        Console.WriteLine("End parse day");

        if (notificationUserList.Count == 0) return;

        _ = Task.Run(() =>
        {
            foreach (var user in notificationUserList)
            {
                _ = this._distributionService.SendDayTimetable(user);
            }

            this._botService.SendAdminMessageAsync(new SendMessageArgs(0,
                $"{notificationUserList.Count} notifications sent"));
        });
    }

    private Task ParseWeek()
    {
        Console.WriteLine("Start parse week");

        var (service, options, delay) = this._firefoxService.Create();
        using (FirefoxDriver driver = new FirefoxDriver(service, options, delay))
        {
            driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(120));

            driver.Navigate().GoToUrl(WeekUrl);
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
            var weekInterval = h3[0].Text;
            if (_weekInterval is null) _weekInterval = weekInterval;
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

    public async Task UpdateTimetableTick()
    {
        Console.WriteLine("Start update tick");
        try
        {
            bool parseDay = false, parseWeek = false;
            var (service, options, delay) = this._firefoxService.Create();
            using (FirefoxDriver driver = new FirefoxDriver(service, options, delay))
            {
                //Day
                driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                driver.Navigate().GoToUrl(DayUrl);
                Thread.Sleep(DriverTimeout);
                var contentElement = driver.FindElement(By.XPath("//*[@id=\"wrapperTables\"]"));
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