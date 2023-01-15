using System.Drawing;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace StudentsTimetable.Services;

public static class Utils
{
    public static ExcelPackage ByteArrayToObject(byte[] arrBytes)
    {
        using (var memStream = new MemoryStream(arrBytes))
        {
            var package = new ExcelPackage(memStream);
            return package;
        }
    }

    public static string HtmlTagsFix(string input)
    {
        return Regex.Replace(input, "<[^>]+>|&nbsp;", "").Trim();
    }

    public static void ModifyUnnecessaryElementsOnWebsite(ref ChromeDriver driver)
    {
        var container = driver.FindElement(By.ClassName("main"));
        driver.ExecuteScript("arguments[0].style='width: 100%; border-top: none'", container);

        driver.Manage().Window.Size = new Size(1920, container.Size.Height - 175);

        var header = driver.FindElement(By.Id("header"));
        driver.ExecuteScript("arguments[0].style='display: none'", header);

        var footer = driver.FindElement(By.Id("footer"));
        driver.ExecuteScript("arguments[0].style='display: none'", footer);
            
        var breadcrumbs = driver.FindElement(By.ClassName("breadcrumbs"));
        driver.ExecuteScript("arguments[0].style='display: none'", breadcrumbs);
            
        var pageShareButtons = driver.FindElement(By.ClassName("page_share_buttons"));
        driver.ExecuteScript("arguments[0].style='display: none'", pageShareButtons);

        var all = driver.FindElement(By.CssSelector("*"));
        driver.ExecuteScript("arguments[0].style='overflow-y: hidden; overflow-x: hidden'", all);
    }
}