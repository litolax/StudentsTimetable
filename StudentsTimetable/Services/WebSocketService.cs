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

    private class APIService : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            Console.WriteLine("Received: " + e.Data);
            Send("Send data by WebSocket");
        }
    }

    public void Connect()
    {
        var wssv = new WebSocketServer(this._config.Entries.WebSocketUrl);
        wssv.AddWebSocketService<APIService>("/api");

        wssv.Start();
        Console.WriteLine($"WebSocket Server started on {this._config.Entries.WebSocketUrl}.");
    }
}