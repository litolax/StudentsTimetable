using System.Text.RegularExpressions;
using MongoDB.Driver;
using Newtonsoft.Json;
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
    private DateTime?[]? _weekInterval;
    private List<string> _thHeaders;
    private const string WeekUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-неделю";
    private const string DayUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-день";
    private const string StatePath = "last.json";
    private static string LastDayHtmlContent { get; set; }
    private static string LastWeekHtmlContent { get; set; }

    private const int DriverTimeout = 2000;

    private bool IsNewInterval;
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
        LoadState(StatePath);
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
            if (groupsAndLessons.Count > 0)
            {
                day = groupsAndLessons[0].Text.Split('-')[1].Trim();
                var tempDay =
                    _thHeaders.FirstOrDefault(th => th.Contains(day, StringComparison.InvariantCultureIgnoreCase)) ??
                    day;
                var daytime = Utils.ParseDateTime(tempDay.Split(", ")[1].Trim());
                if (daytime?.DayOfWeek is DayOfWeek.Saturday && !Utils.IsDateBelongsToInterval(daytime, _weekInterval))
                {
                    Console.WriteLine("End parse day(next saturday)");
                    await this._botService.SendAdminMessageAsync(new SendMessageArgs(0,
                        "Detected next Saturday!" + tempDay));
                    return;
                }

                day = tempDay;
            }

            try
            {
                for (var i = 1; i < groupsAndLessons.Count; i += 2)
                {
                    var parsedGroupName = groupsAndLessons[i - 1].Text.Split('-')[0].Trim();
                    group = this.Groups.FirstOrDefault(g => g == parsedGroupName);
                    if (group is null) continue;
                    var groupInfo = new GroupInfo();
                    var lessons = new List<Lesson>();

                    var lessonsElements =
                        groupsAndLessons[i].FindElements(By.XPath(".//table/tbody/tr | .//p")).ToList();

                    if (lessonsElements.Count < 1 || lessonsElements[0].TagName == "p")
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

        var notificationUserHashSet = new HashSet<User>();
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
                    foreach (var notificationUser in (await this._mongoService.Database.GetCollection<User>("Users")
                                 .FindAsync(u => u.Groups != null && u.Notifications)).ToList().Where(u =>
                             {
                                 if (u?.Groups != null &&
                                     int.TryParse(Regex.Replace(u.Groups?.FirstOrDefault(g =>
                                             g == groupInfo.Number.ToString()) ?? string.Empty, "[^0-9]", ""),
                                         out int userGroupNumber))
                                     return userGroupNumber == groupInfo.Number;
                                 return false;
                             }).ToList())
                        notificationUserHashSet.Add(notificationUser);
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
                foreach (var notificationUser in (await this._mongoService.Database.GetCollection<User>("Users")
                             .FindAsync(u => u.Groups != null && u.Notifications)).ToList().Where(u =>
                         {
                             if (u?.Groups != null &&
                                 int.TryParse(
                                     Regex.Replace(
                                         u.Groups?.FirstOrDefault(g => g == groupInfo.Number.ToString()) ??
                                         string.Empty,
                                         "[^0-9]", ""), out int userGroupNumber))
                                 return userGroupNumber == groupInfo.Number;
                             return false;
                         }).ToList()) notificationUserHashSet.Add(notificationUser);
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

        if (notificationUserHashSet.Count == 0) return;

        _ = Task.Run(() =>
        {
            foreach (var user in notificationUserHashSet)
            {
                _ = this._distributionService.SendDayTimetable(user);
            }

            this._botService.SendAdminMessageAsync(new SendMessageArgs(0,
                $"{day}:{notificationUserHashSet.Count} notifications sent"));
        });
    }

    private async Task ParseWeek()
    {
        Console.WriteLine("Start parse week");
        var (service, options, delay) = this._firefoxService.Create();
        using FirefoxDriver driver = new FirefoxDriver(service, options, delay);
        driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(120));
        IsNewInterval = false;
        driver.Navigate().GoToUrl(WeekUrl);
        var element = driver.FindElement(By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div"));
        wait.Until(d => element.Displayed);
        Utils.ModifyUnnecessaryElementsOnWebsite(driver);

        if (element == default) return;
        var h2 =
            driver.FindElements(
                By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div/h2"));

        var h3 =
            driver.FindElements(
                By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div/h3"));
        var weekIntervalStr = h3[0].Text;
        var weekInterval = Utils.ParseDateTimeWeekInterval(weekIntervalStr);
        var isIsNewInterval = IsNewInterval;
        if (_weekInterval is null || !string.IsNullOrEmpty(weekIntervalStr) && _weekInterval != weekInterval)
        {
            IsNewInterval = _weekInterval is not null;
            if (_weekInterval is null || _weekInterval[1] is not null && DateTime.Today == _weekInterval[1])
            {
                _weekInterval = weekInterval;
                IsNewInterval = false;
                Console.WriteLine("New interval is " + weekIntervalStr);
                this._botService.SendAdminMessage(new SendMessageArgs(0, "New interval is " + weekIntervalStr));
            }

            isIsNewInterval = !isIsNewInterval && IsNewInterval;
            var tempThHeaders = driver
                .FindElement(By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div/div[1]/table/tbody/tr[1]"))
                .FindElements(By.TagName("th"));
            _thHeaders = new List<string>();
            foreach (var thHeader in tempThHeaders) _thHeaders.Add(new string(thHeader.Text));
        }

        var table = driver.FindElements(By.XPath("/html/body/div[1]/div[2]/div/div[2]/div[1]/div/div"));
        Utils.HideGroupElements(driver, h3);
        Utils.HideGroupElements(driver, h2);
        Utils.HideGroupElements(driver, table);
        var notificationUserHashSet = new HashSet<User>();
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
                _ = image.SaveAsync($"./cachedImages/{groupName.Replace("*", "knor")}.png");
                if (IsNewInterval)
                    foreach (var notificationUser in (await this._mongoService.Database.GetCollection<User>("Users")
                                 .FindAsync(u => u.Groups != null && u.Notifications)).ToList())
                        notificationUserHashSet.Add(notificationUser);
            }
            catch (Exception e)
            {
                this._botService.SendAdminMessage(new SendMessageArgs(0,
                    e.Message + "\nОшибка в группе: " + groupName));
            }
            finally
            {
                Utils.HideGroupElements(driver, list);
            }
        }

        if (isIsNewInterval)
            _ = Task.Run(() =>
            {
                foreach (var user in notificationUserHashSet)
                    _ = this._botService.SendMessageAsync(new SendMessageArgs(user.UserId,
                        $"У групп{(user.Groups.Length > 1 ? "" : "ы")} {Utils.GetGroupsString(user!.Groups)} вышло новое недельное расписание. Нажмите \"Посмотреть расписание на неделю\" для просмотра недельного расписания."));
                this._botService.SendAdminMessageAsync(new SendMessageArgs(0,
                    $"{weekIntervalStr}:{notificationUserHashSet.Count} notifications sent"));
            });
        Console.WriteLine("End week parse");
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

            if (parseWeek || parseDay) SaveState(StatePath);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        Console.WriteLine("End update tick");
    }

    private void SaveState(string filePath)
    {
        var stateToSave = new
        {
            WeekInterval = _weekInterval,
            ThHeaders = _thHeaders,
            LastDayHtmlContent,
            LastWeekHtmlContent,
            Timetable
        };

        string json = JsonConvert.SerializeObject(stateToSave);
        File.WriteAllText(filePath, json);
    }

    private void LoadState(string filePath)
    {
        if (!File.Exists(filePath)) return;
        var state = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(filePath));
        _weekInterval = state!.WeekInterval.ToObject<DateTime?[]>();
        _thHeaders = state.ThHeaders.ToObject<List<string>>();
        LastDayHtmlContent = state.LastDayHtmlContent;
        LastWeekHtmlContent = state.LastWeekHtmlContent;
        Timetable = state.Timetable.ToObject<List<Day>>();
    }
}