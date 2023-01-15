using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MongoDB.Driver;
using OfficeOpenXml;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StudentsTimetable.Config;
using StudentsTimetable.Models;
using Syncfusion.XlsIO;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableMethods.FormattingOptions;
using Telegram.BotAPI.AvailableTypes;
using File = System.IO.File;
using Size = System.Drawing.Size;
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
    Task SendNewDayTimetables();
}

public class ParserService : IParserService
{
    private readonly IMongoService _mongoService;

    private const string WeekUrl = "https://mgkct.minskedu.gov.by/персоналии/учащимся/расписание-занятий-на-неделю";

    public static bool ParseResult;
    private string LastDayHtmlContent { get; set; }
    private string LastWeekHtmlContent { get; set; }

    public List<string> Groups { get; set; } = new()
    {
        "7", "8", "41", "42", "44", "45", "46", "47", "49", "50", "52", "53", "54", "55", "56", "60", "61", "63", "64",
        "65",
        "66", "67", "68", "69", "70", "71", "72", "73", "74", "75", "76", "77", "78", "156", "157", "160", "161", "162",
        "163", "164"
    };

    public List<Day> Timetables { get; set; } = new();

    public ParserService(IMongoService mongoService)
    {
        this._mongoService = mongoService;

        var parseDayTimer = new Timer(10000)
        {
            AutoReset = true, Enabled = true
        };
        parseDayTimer.Elapsed += async (sender, args) => { await this.NewDayTimetableCheck(); };

        var parseWeekTimer = new Timer(10000)
        {
            AutoReset = true, Enabled = true
        };
        parseWeekTimer.Elapsed += async (sender, args) => { await this.NewWeekTimetableCheck(); };
    }

    public async Task ParseDayTimetables()
    {
        ParseResult = false;
        var parseInfo = (await this._mongoService.Database.GetCollection<Info>("Info").FindAsync(i => true)).ToList()
            .First();
        if (!parseInfo.ParseAllowed) return;
        List<Day> Days = new List<Day>();
        List<GroupInfo> groupInfos = new List<GroupInfo>();
        var url = "http://mgke.minsk.edu.by/ru/main.aspx?guid=3831";
        var web = new HtmlWeb();
        var doc = web.Load(url);
        this.LastDayHtmlContent = doc.DocumentNode.InnerHtml;

        var tableToExcel = new HtmlTableToExcel.HtmlTableToExcel();
        byte[] converted = tableToExcel.Process(doc.Text);
        var obj = ByteArrayToObject(converted);
        obj.SaveAs(new FileInfo("./timetable.xlsx"));

        var excelEngine = new ExcelEngine();

        await using (var stream = File.Open(parseInfo.LoadFixFile ? "./fix.xlsx" : "./timetable.xlsx", FileMode.Open,
                         FileAccess.ReadWrite))
        {
            var workbook = excelEngine.Excel.Workbooks.Open(stream);
            var firstSheet = workbook.Worksheets["sheet1"];

            firstSheet.InsertColumn(firstSheet.Columns.Length + 1, 3);
            firstSheet.InsertRow(firstSheet.Rows.Length + 1, 20);

            try
            {
                #region ParseDayNamesAndIndexes

                var groupTableWithIndexesWithoutSame =
                    new Dictionary<int, string>(); //парсим тут дни недели и их индексы в таблиуе
                for (int i = 0; i < firstSheet.Rows.Length; i++)
                {
                    var parsedValue = HtmlTagsFix(firstSheet.Rows[i].Cells[0].Value);
                    if ((!parsedValue.Contains("День")) ||
                        groupTableWithIndexesWithoutSame.ContainsValue(parsedValue)) continue;

                    groupTableWithIndexesWithoutSame.Add(i, parsedValue);
                }

                var groupTableWithIndexesWithSame =
                    new Dictionary<int, string>(); //парсим тут дни недели и их индексы в таблиуе
                for (int i = 0; i < firstSheet.Rows.Length; i++)
                {
                    var parsedValue = HtmlTagsFix(firstSheet.Rows[i].Cells[0].Value);
                    if (!parsedValue.Contains("День")) continue;

                    groupTableWithIndexesWithSame.Add(i, parsedValue);
                }

                #endregion

                #region ParseLessonsIndexes

                var startLessonsIndexes =
                    new Dictionary<int, string>(); //парсим тут пары
                for (int i = 0; i < firstSheet.Rows.Length; i++)
                {
                    var newValue = HtmlTagsFix(firstSheet.Rows[i].Cells[0].Value);
                    if (!newValue.Contains("Дисциплина")) continue;

                    startLessonsIndexes.Add(i, newValue);
                }

                #endregion

                string lastContent = string.Empty;

                for (int i = 0; i < groupTableWithIndexesWithSame.Count; i++)
                {
                    for (int j = 1;
                         int.TryParse(
                             firstSheet.Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1].Cells[j].DisplayText,
                             out _);
                         j++)
                    {
                        string lessonName = string.Empty;
                        string cabinet = string.Empty;
                        var groupInfo = new GroupInfo();
                        int lessonIndex = 0;

                        var parsedValue = firstSheet.Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1].Cells[j]
                            .DisplayText;
                        if (string.Equals(lastContent, parsedValue)) continue;

                        groupInfo.Date = groupTableWithIndexesWithSame.Values.ToList()[i];
                        groupInfo.Number = int.Parse(parsedValue);
                        //сверху мы спарсили саму группу

                        int lessonsCount;

                        if (groupTableWithIndexesWithSame.Count <= i + 1) lessonsCount = 10;
                        else
                            lessonsCount = groupTableWithIndexesWithSame.Keys.ToList()[i + 1] -
                                           (groupTableWithIndexesWithSame.Keys.ToList()[i] + 4);

                        for (int h = 0; h < lessonsCount; h++)
                        {
                            cabinet = firstSheet
                                .Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1 + 1 + 2 + lessonIndex]
                                .Cells[j + 1]
                                .RichText.Text;
                            lessonName = firstSheet
                                .Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1 + 1 + 2 + lessonIndex]
                                .Cells[j + 1 - 1]
                                .RichText.Text;

                            if (string.Equals(cabinet, lessonName))
                            {
                                int index = 1;
                                while (string.Equals(cabinet, lessonName) && cabinet.Length > 1 &&
                                       lessonName.Length > 1)
                                {
                                    cabinet = firstSheet
                                        .Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1 + 1 + 2 + lessonIndex]
                                        .Cells[j + 1 + index].DisplayText;
                                    lessonName = firstSheet
                                        .Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1 + 1 + 2 + lessonIndex]
                                        .Cells[j + 1 - 1].DisplayText;
                                    index++;
                                }
                            }

                            groupInfo.Lessons.Add(new Lesson()
                            {
                                Group = parsedValue,
                                Cabinet = cabinet,
                                Name = lessonName,
                                Number = lessonIndex
                            });

                            lessonIndex++;
                        }

                        //обновили инфу, идем дальше
                        lastContent = parsedValue;
                        groupInfos.Add(groupInfo);
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
                }

                for (int i = 0; i < groupTableWithIndexesWithoutSame.Count; i++)
                {
                    var date = groupTableWithIndexesWithoutSame.Values.ToList()[i];
                    var info = groupInfos.Where(g => g.Date == groupTableWithIndexesWithoutSame.Values.ToList()[i])
                        .ToList();
                    Days.Add(new Day()
                    {
                        Date = date,
                        GroupInfos = info
                    });
                }

                lock (this.Timetables) this.Timetables = Days;
            }
            catch (Exception e)
            {
                return;
            }
        }

        bool hasNewTimetables = false;
        var timetablesCollection = this._mongoService.Database.GetCollection<Day>("DayTimetables");
        var dbTables = (await timetablesCollection.FindAsync(table => true)).ToList();

        this.Timetables.ForEach(t =>
        {
            if (!dbTables.Exists(table => table.Date == t.Date))
            {
                timetablesCollection.InsertOneAsync(t);
                hasNewTimetables = true;
            }
        });

        if (hasNewTimetables)
        {
            await this.SendNewDayTimetables();
        }

        ParseResult = true;
    }

    public async Task SendNewDayTimetables()
    {
        var config = new Config<MainConfig>();
        var bot = new BotClient(config.Entries.Token);

        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var users = (await userCollection.FindAsync(u => true)).ToList();

        foreach (var user in users)
        {
            if (!user.Notifications || user.Group is null) continue;
            if (this.Timetables.Count < 1)
            {
                try
                {
                    await bot.SendMessageAsync(user.UserId, $"У {user.Group} группы нет пар");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

                return;
            }

            foreach (var day in this.Timetables)
            {
                var message = day.Date + "\n";
                foreach (var groupInfo in day.GroupInfos)
                {
                    if (int.Parse(user.Group) != groupInfo.Number) continue;

                    if (groupInfo.Lessons.Count < 1)
                    {
                        message = $"У {groupInfo.Number} группы нет пар на {day.Date}";
                        continue;
                    }

                    message += $"Группа: *{user.Group}*\n\n";

                    foreach (var lesson in groupInfo.Lessons)
                    {
                        var lessonName = HtmlTagsFix(lesson.Name).Replace('\n', ' ');
                        var cabinet = HtmlTagsFix(lesson.Cabinet).Replace('\n', ' ');
                        var newlineIndexes = new List<int>();
                        for (int g = 0; g < lessonName.Length; g++)
                        {
                            if (int.TryParse(lessonName[g].ToString(), out _) && g != 0)
                            {
                                newlineIndexes.Add(g);
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
                            $"*Пара: №{lesson.Number + 1}*" +
                            $"\n{(lessonName.Length < 2 ? "-" : lessonName)}" +
                            $"\n{(cabinet.Length < 2 ? "-" : ($"Каб: {cabinet}"))}" +
                            $"\n\n";
                    }
                }

                try
                {
                    await bot.SendMessageAsync(user.UserId, message, parseMode: ParseMode.Markdown);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
    }

    public async Task SendDayTimetable(User telegramUser)
    {
        var config = new Config<MainConfig>();
        var bot = new BotClient(config.Entries.Token);

        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
        if (user is null) return;

        if (user.Group is null)
        {
            try
            {
                await bot.SendMessageAsync(user.UserId, "Вы еще не выбрали группу");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return;
        }

        if (this.Timetables.Count < 1)
        {
            try
            {
                await bot.SendMessageAsync(user.UserId, $"У {user.Group} группы нет пар");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return;
        }

        foreach (var day in this.Timetables)
        {
            var message = day.Date + "\n";
            foreach (var groupInfo in day.GroupInfos)
            {
                if (int.Parse(user.Group) != groupInfo.Number) continue;

                if (groupInfo.Lessons.Count < 1)
                {
                    message = $"У {groupInfo.Number} группы нет пар на {day.Date}";
                    continue;
                }

                message += $"Группа: *{user.Group}*\n\n";

                foreach (var lesson in groupInfo.Lessons)
                {
                    var lessonName = HtmlTagsFix(lesson.Name).Replace('\n', ' ');
                    var cabinet = HtmlTagsFix(lesson.Cabinet).Replace('\n', ' ');
                    var newlineIndexes = new List<int>();
                    for (int g = 0; g < lessonName.Length; g++)
                    {
                        if (int.TryParse(lessonName[g].ToString(), out _) && g != 0)
                        {
                            newlineIndexes.Add(g);
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
                        $"*Пара: №{lesson.Number + 1}*" +
                        $"\n{(lessonName.Length < 2 ? "-" : lessonName)}" +
                        $"\n{(cabinet.Length < 2 ? "-" : ($"Каб: {cabinet}"))}" +
                        $"\n\n";
                }
            }

            try
            {
                await bot.SendMessageAsync(user.UserId, message, parseMode: ParseMode.Markdown);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
    
    public async Task ParseWeekTimetables()
    {
        var web = new HtmlWeb();
        var doc = web.Load(WeekUrl);

        this.LastWeekHtmlContent = doc.DocumentNode.InnerHtml;

        var students = doc.DocumentNode.SelectNodes("//h2");
        if (students is null) return;

        var newDate = doc.DocumentNode.SelectNodes("//h3")[0].InnerText.Trim();
        var dateDbCollection = this._mongoService.Database.GetCollection<Timetable>("WeekTimetables");
        var dbTables = (await dateDbCollection.FindAsync(d => true)).ToList();

        ChromeOptions options = new ChromeOptions();

        options.AddArgument("headless");
        options.AddArgument("--no-sandbox");

        var driver = new ChromeDriver(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), options);
        driver.Manage().Window.Size = new Size(1920, 1250);

        foreach (var group in Groups)
        {
            var filePath = $"./photo/Группа - {group}.png";
            
            driver.Navigate().GoToUrl($"{WeekUrl}?group={group}");

            var container = driver.FindElement(By.ClassName("main"));
            driver.ExecuteScript("arguments[0].style='width: 100%'", container);

            var element = driver.FindElements(By.TagName("h2")).FirstOrDefault();
            if (element == default) continue;
            
            var actions = new Actions(driver);
            actions.MoveToElement(element).Perform();

            actions.ScrollByAmount(0, 325).Perform();

            var screenshot = (driver as ITakesScreenshot).GetScreenshot();
            screenshot.SaveAsFile(filePath, ScreenshotImageFormat.Png);
            
            var image = await Image.LoadAsync(filePath);
        
            image.Mutate(x => x.Resize((int)(image.Width / 1.5), (int)(image.Height / 1.5)));
            await image.SaveAsPngAsync(filePath);
        }


        driver.Close();
        driver.Quit();

        if (!dbTables.Exists(table => table.Date.Trim() == newDate))
        {
            await dateDbCollection.InsertOneAsync(new Timetable()
            {
                Date = newDate
            });
            await this.SendNotificationsAboutWeekTimetable();
        }
    }

    public async Task SendWeekTimetable(User telegramUser)
    {
        var config = new Config<MainConfig>();
        var bot = new BotClient(config.Entries.Token);

        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var user = (await userCollection.FindAsync(u => u.UserId == telegramUser.Id)).ToList().First();
        if (user is null) return;

        if (user.Group is null)
        {
            try
            {
                await bot.SendMessageAsync(user.UserId, "Вы еще не выбрали группу");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return;
        }

        
        var image = await Image.LoadAsync($"./photo/Группа - {user.Group}.png");

        using (var ms = new MemoryStream())
        {
            await image.SaveAsync(ms, new PngEncoder());

            try
            {
                await bot.SendPhotoAsync(user.UserId, new InputFile(ms.ToArray(), $"./photo/Группа - {user.Group}.png"));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    public async Task SendNotificationsAboutWeekTimetable()
    {
        var config = new Config<MainConfig>();
        var bot = new BotClient(config.Entries.Token);

        var userCollection = this._mongoService.Database.GetCollection<Models.User>("Users");
        var users = (await userCollection.FindAsync(u => true)).ToList();
        if (users is null) return;

        foreach (var user in users)
        {
            if (user.Group is null || !user.Notifications) continue;

            try
            {
                await bot.SendMessageAsync(user.UserId, "Обновлено расписание на неделю");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    private static ExcelPackage ByteArrayToObject(byte[] arrBytes)
    {
        using (var memStream = new MemoryStream(arrBytes))
        {
            var package = new ExcelPackage(memStream);
            return package;
        }
    }

    private static string HtmlTagsFix(string input)
    {
        return Regex.Replace(input, "<[^>]+>|&nbsp;", "").Trim();
    }

    private async Task NewDayTimetableCheck()
    {
        var url = "http://mgke.minsk.edu.by/ru/main.aspx?guid=3831";
        var web = new HtmlWeb();
        var doc = web.Load(url);
        if (this.LastDayHtmlContent == doc.DocumentNode.InnerHtml) return;

        await this.ParseDayTimetables();
    }

    private async Task NewWeekTimetableCheck()
    {
        var web = new HtmlWeb();
        var doc = web.Load(WeekUrl);
        if (this.LastWeekHtmlContent == doc.DocumentNode.InnerHtml) return;

        await this.ParseWeekTimetables();
    }
}