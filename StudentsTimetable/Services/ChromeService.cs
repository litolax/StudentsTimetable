using System.Diagnostics;
using OpenQA.Selenium.Chrome;

namespace StudentsTimetable.Services;

public interface IChromeService
{
}

public class ChromeService : IChromeService
{
    public static ChromeDriver Driver { get; set; }
    public static Process Process { get; set; }
    
    public ChromeService()
    {
        (Driver, Process) = Utils.CreateChromeDriver();
    }
    
    
}