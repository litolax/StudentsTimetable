using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MongoDB.Driver;
using OfficeOpenXml;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using SixLabors.ImageSharp.Formats.Png;
using StudentsTimetable.Config;
using StudentsTimetable.Models;
using Syncfusion.XlsIO;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableMethods.FormattingOptions;
using Telegram.BotAPI.AvailableTypes;
using File = System.IO.File;
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

    public List<string> Groups { get; set; } = new()
    {
        "41", "42", "44", "45", "46", "47", "49", "50", "52", "53", "54", "55", "56", "60", "61", "63", "64", "65",
        "66", "67", "68", "71", "72", "73", "74", "75", "76", "77", "78"
    };

    public List<Day>? Timetables { get; set; } = new();

    public ParserService(IMongoService mongoService)
    {
        this._mongoService = mongoService;
        var parseDayTimer = new Timer(300000)
        {
            AutoReset = true, Enabled = true
        };
        parseDayTimer.Elapsed += async (sender, args) => { await this.ParseDayTimetables(); };

        var parseWeekTimer = new Timer(2000000)
        {
            AutoReset = true, Enabled = true
        };
        parseWeekTimer.Elapsed += async (sender, args) => { await this.ParseWeekTimetables(); };
    }

    public async Task ParseDayTimetables()
    {
        List<Day> Days = new List<Day>();
        List<GroupInfo> groupInfos = new List<GroupInfo>();
        var url = "http://mgke.minsk.edu.by/ru/main.aspx?guid=3831";
        var web = new HtmlWeb();
        var doc = web.Load(url);

        var tableToExcel = new HtmlTableToExcel.HtmlTableToExcel();
        byte[] converted = tableToExcel.Process(doc.Text);
        var obj = ByteArrayToObject(converted);
        obj.SaveAs(new FileInfo("./timetable.xlsx"));

        var excelEngine = new ExcelEngine();

        await using (var stream = File.Open("./timetable.xlsx", FileMode.Open, FileAccess.ReadWrite))
        {
            var workbook = excelEngine.Excel.Workbooks.Open(stream);
            var firstSheet = workbook.Worksheets["sheet1"];

            firstSheet.InsertColumn(firstSheet.Columns.Length, 3);
            firstSheet.InsertRow(firstSheet.Rows.Length, 20);

            try
            {
                #region ParseDayNamesAndIndexes

                var groupTableWithIndexesWithoutSame =
                    new Dictionary<int, string>(); //парсим тут дни недели и их индексы в таблиуе
                for (int i = 0; i < firstSheet.Rows.Length; i++)
                {
                    var newValue = RegexCostyl(firstSheet.Rows[i].Cells[0].Value);
                    if ((!newValue.Contains("День")) ||
                        groupTableWithIndexesWithoutSame.ContainsValue(newValue)) continue;

                    groupTableWithIndexesWithoutSame.Add(i, newValue);
                }

                var groupTableWithIndexesWithSame =
                    new Dictionary<int, string>(); //парсим тут дни недели и их индексы в таблиуе
                for (int i = 0; i < firstSheet.Rows.Length; i++)
                {
                    var newValue = RegexCostyl(firstSheet.Rows[i].Cells[0].Value);
                    if (!newValue.Contains("День")) continue;

                    groupTableWithIndexesWithSame.Add(i, newValue);
                }

                #endregion

                #region ParseLessonsIndexes

                var startLessonsIndexes =
                    new Dictionary<int, string>(); //парсим тут дни недели и их индексы в таблиуе
                for (int i = 0; i < firstSheet.Rows.Length; i++)
                {
                    var newValue = RegexCostyl(firstSheet.Rows[i].Cells[0].Value);
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
                        string kabinet = string.Empty;
                        var groupInfo = new GroupInfo();
                        int lessonIndex = 0;

                        var newValue = firstSheet.Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1].Cells[j]
                            .DisplayText;
                        if (string.Equals(lastContent, newValue)) continue;

                        Console.WriteLine(newValue);
                        groupInfo.Date = groupTableWithIndexesWithSame.Values.ToList()[i];
                        groupInfo.Number = int.Parse(newValue);
                        //сверху мы спарсили саму группу

                        int lessonsCount;

                        if (groupTableWithIndexesWithSame.Count <= i + 1) lessonsCount = 10;
                        else
                            lessonsCount = groupTableWithIndexesWithSame.Keys.ToList()[i + 1] -
                                           (groupTableWithIndexesWithSame.Keys.ToList()[i] + 4);

                        for (int h = 0; h < lessonsCount; h++)
                        {
                            kabinet = firstSheet
                                .Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1 + 1 + 2 + lessonIndex]
                                .Cells[j + 1]
                                .DisplayText;
                            lessonName = firstSheet
                                .Rows[groupTableWithIndexesWithSame.Keys.ToList()[i] + 1 + 1 + 2 + lessonIndex]
                                .Cells[j + 1 - 1].DisplayText;

                            if (string.Equals(kabinet, lessonName))
                            {
                                int index = 1;
                                while (string.Equals(kabinet, lessonName) && kabinet.Length > 1 &&
                                       lessonName.Length > 1)
                                {
                                    kabinet = firstSheet
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
                                Group = newValue,
                                Kabinet = kabinet,
                                Name = lessonName,
                                Number = lessonIndex
                            });

                            int count = 0;
                            groupInfo.Lessons.Reverse();
                            foreach (var lesson in groupInfo.Lessons)
                            {
                                if (lesson.Name.Length < 1) count++;
                                else break;
                            }

                            groupInfo.Lessons.RemoveRange(0, count);
                            groupInfo.Lessons.Reverse();

                            lessonIndex++;
                        }

                        //обновили инфу, идем дальше
                        lastContent = newValue;
                        groupInfos.Add(groupInfo);
                    }
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

                this.Timetables = Days;
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
            if (this.Timetables is null)
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

                    message += $"Группа: {user.Group}\n\n";

                    foreach (var lesson in groupInfo.Lessons)
                    {
                        message +=
                            $"Пара: №{lesson.Number + 1}\n\n{RegexCostyl(lesson.Name).Replace('\n', ' ')}\nКаб: {RegexCostyl(lesson.Kabinet)}\n\n";
                    }
                }

                try
                {
                    await bot.SendMessageAsync(user.UserId, message);
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

        if (this.Timetables is null)
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

                message += $"Группа: {user.Group}\n\n";

                foreach (var lesson in groupInfo.Lessons)
                {
                    message +=
                        $"Пара: №{lesson.Number + 1}\n\n{RegexCostyl(lesson.Name).Replace('\n', ' ')}\nКаб: {RegexCostyl(lesson.Kabinet)}\n\n";
                }
            }

            try
            {
                await bot.SendMessageAsync(user.UserId, message);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    public async Task ParseWeekTimetables()
    {
        var url = "http://mgke.minsk.edu.by/ru/main.aspx?guid=3781";
        var web = new HtmlWeb();
        var doc = web.Load(url);

        var students = doc.DocumentNode.SelectNodes("//h2");
        if (students is null) return;

        var newDate = doc.DocumentNode.SelectNodes("//h3")[0];
        var dateDbCollection = this._mongoService.Database.GetCollection<Timetable>("WeekTimetables");
        var dbTables = (await dateDbCollection.FindAsync(d => true)).ToList();

        ChromeOptions options = new ChromeOptions();
        options.AddArgument("headless");
        options.AddArgument("--no-sandbox");
        var driver = new ChromeDriver(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), options);
        driver.Manage().Window.Size = new Size(1200, 1250);

        driver.Navigate().GoToUrl("http://mgke.minsk.edu.by/ru/main.aspx?guid=3781");

        var elements = driver.FindElements(By.TagName("h2"));

        for (int i = 0; i < elements.Count; i++)
        {
            Actions actions = new Actions(driver);
            if (i + 1 < elements.Count) actions.MoveToElement(elements[i + 1]).Perform();
            else
            {
                actions.MoveToElement(elements[i]).Perform();
                for (int j = 0; j < 100; j++)
                {
                    actions.SendKeys(Keys.Down);
                }
            }

            actions.Perform();

            var screenshot = (driver as ITakesScreenshot).GetScreenshot();
            screenshot.SaveAsFile($"./photo/{students[i].ChildNodes[0].InnerHtml}.png",
                ScreenshotImageFormat.Png);
        }

        driver.Close();
        driver.Quit();

        if (!dbTables.Exists(table => table.Date.Trim() == newDate.InnerText.Trim()))
        {
            await dateDbCollection.InsertOneAsync(new Timetable()
            {
                Date = newDate.InnerText.Trim()
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


        var image = await SixLabors.ImageSharp.Image.LoadAsync($"./photo/Группа - {user.Group}.png");
        using (var ms = new MemoryStream())
        {
            await image.SaveAsync(ms, new PngEncoder());

            try
            {
                await bot.SendPhotoAsync(user.UserId, new InputFile(ms.ToArray(), $"./photo/{user.Group}.png"));
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
        using (MemoryStream memStream = new MemoryStream(arrBytes))
        {
            ExcelPackage package = new ExcelPackage(memStream);
            return package;
        }
    }

    private static string RegexCostyl(string input)
    {
        return Regex.Replace(input, "<[^>]+>|&nbsp;", "").Trim();
    }
}