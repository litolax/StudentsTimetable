using System.Timers;
using Timer = System.Timers.Timer;

namespace StudentsTimetable.Services
{
    public interface IAntiSpamService
    {
        void AddToSpam(long userId);
        bool IsSpammer(long userId);
    }

    public class AntiSpamService : IAntiSpamService
    {
        private Dictionary<long, DateTime> Spammers { get; set; } = new();
        private Timer _timer = new(10000) {AutoReset = true, Enabled = true};

        public AntiSpamService()
        {
            this._timer.Elapsed += this.ValidationSpammersTimeout;
        }

        private void ValidationSpammersTimeout(object? sender, ElapsedEventArgs e)
        {
            foreach (var (spammerId, timeoutTime) in this.Spammers)
            {
                if (DateTime.UtcNow > timeoutTime.AddMinutes(2)) this.Spammers.Remove(spammerId);
            }
        }

        public void AddToSpam(long userId)
        {
            if (this.Spammers.ContainsKey(userId)) return;
            this.Spammers.Add(userId, DateTime.UtcNow);
        }

        public bool IsSpammer(long userId)
        {
            return this.Spammers.ContainsKey(userId);
        }
    }
}