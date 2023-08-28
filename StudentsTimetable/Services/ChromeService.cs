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
        options.AddArgument("--headless");
        options.AddArgument("--log-level=3");
        options.AddArgument("--force-device-scale-factor=1");
        
        var driver = new FirefoxDriver("./", options,TimeSpan.FromSeconds(120));
        driver.Manage().Timeouts().PageLoad += TimeSpan.FromMinutes(3);

        return driver;
    }
}