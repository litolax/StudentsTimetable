﻿using OpenQA.Selenium;
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

        var options = new FirefoxOptions
        {
            PageLoadStrategy = PageLoadStrategy.Eager
        };

        options.AddArgument("--no-sandbox");
        options.AddArgument("no-sandbox");
        options.AddArgument("--headless");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--disable-crash-reporter");
        options.AddArgument("--disable-extensions");
        options.AddArgument("--disable-in-process-stack-traces");
        options.AddArgument("--disable-logging");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--log-level=3");
        options.AddArgument("--output=/dev/null");
        options.AddArgument("--force-device-scale-factor=1");
        options.AddArgument("--disable-browser-side-navigation");
        options.SetEnvironmentVariable("webdriver.gecko.driver", "./geckodriver");
        
        var driver = new FirefoxDriver(service, options,TimeSpan.FromMinutes(2));
        driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromMinutes(2));

        return driver;
    }
}