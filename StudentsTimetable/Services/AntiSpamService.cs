using System.Timers;
using Timer = System.Timers.Timer;

namespace StudentsTimetable.Services
{
    public interface IAntiSpamService
    {
        Task AddToSpam(long userId);
        Task<bool> IsSpammer(long userId);
    }

    public class AntiSpamService : IAntiSpamService
    {
        private Dictionary<long, DateTime> Spammers { get; set; } = new();
        private Timer _timer = new(10000) {AutoReset = true, Enabled = true};

        public AntiSpamService()
        {
            this._timer.Elapsed += ValidationSpammersTimeout;
        }

        private void ValidationSpammersTimeout(object sender, ElapsedEventArgs e)
        {
            foreach (var (spammerId, timeoutTime) in this.Spammers)
            {
                if (DateTime.UtcNow > timeoutTime.AddMinutes(2)) this.Spammers.Remove(spammerId);
            }
        }

        public Task AddToSpam(long userId)
        {
            if (this.Spammers.ContainsKey(userId)) return Task.CompletedTask;
            this.Spammers.Add(userId, DateTime.UtcNow);
            return Task.CompletedTask;
        }

        public Task<bool> IsSpammer(long userId)
        {
            return Task.FromResult(this.Spammers.ContainsKey(userId));
        }
    }
}