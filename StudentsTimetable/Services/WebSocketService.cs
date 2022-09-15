using StudentsTimetable.Config;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace StudentsTimetable.Services;

public interface IWebSocketService
{
    void Connect();
}

public class WebSocketService : IWebSocketService
{
    private readonly IConfig<MainConfig> _config;

    public WebSocketService(IConfig<MainConfig> config)
    {
        this._config = config;
    }

    private class BotHealthService : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            Send("botHealth:" + true);
        }
    }
    
    private class ParserHealthService : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            Send("parserHealth:" + ParserService.ParseResult);
        }
    }

    public void Connect()
    {
        var wssv = new WebSocketServer(this._config.Entries.WebSocketUrl);
        wssv.AddWebSocketService<BotHealthService>("/healthCheck/students/bot");
        wssv.AddWebSocketService<ParserHealthService>("/healthCheck/students/parser");

        wssv.Start();
        Console.WriteLine($"WebSocket Server started on {this._config.Entries.WebSocketUrl}.");
    }
}