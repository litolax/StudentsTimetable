using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;

namespace StudentsTimetable.Services;

public interface IChromeService
{
}

public class ChromeService : IChromeService
{
    public static FirefoxDriver Driver { get; set; }
    
    public ChromeService()
    {
        Driver = this.Create();
    }

    private FirefoxDriver Create()
    {
        var service = FirefoxDriverService.CreateDefaultService();
        
        service.SuppressInitialDiagnosticInformation = true;
        service.HideCommandPromptWindow = true;

        var options = new FirefoxOptions();

        options.AddArgument("--no-sandbox");
        options.AddArgument("no-sandbox");
        options.AddArgument("--headless");
        options.AddArgument("--log-level=3");
        options.AddArgument("--force-device-scale-factor=1");
        options.SetEnvironmentVariable("webdriver.gecko.driver", "./geckodriver");
        
        var driver = new FirefoxDriver("./", options,TimeSpan.FromMinutes(2));
        driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));

        return driver;
    }
}