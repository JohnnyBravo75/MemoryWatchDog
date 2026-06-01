namespace MemoryWatchDog
{
    using System;
    using System.Timers;

    public class MemoryWatchDog : IDisposable
    {
        private System.Timers.Timer checkTimer = new System.Timers.Timer();

        private bool isWatching = false;

        public TimeSpan CheckInterval { get; private set; }

        public MemoryGrabber Grabber { get; } = new MemoryGrabber();

        public MemoryWatchDog()
        {
            this.checkTimer.Elapsed += this.CheckTimer_Tick;
        }

        public bool IsWatching
        {
            get { return this.checkTimer != null && this.isWatching; }
        }

        public void StartWatching(TimeSpan checkInterval)
        {
            this.CheckInterval = checkInterval;

            if (this.checkTimer != null && !this.checkTimer.Enabled)
            {
                this.checkTimer.Interval = checkInterval.TotalMilliseconds;
                this.checkTimer.Start();
                this.isWatching = true;
            }
        }

        public void StopWatching()
        {
            if (this.checkTimer != null && this.checkTimer.Enabled)
            {
                this.checkTimer.Stop();
                this.isWatching = false;
            }
        }

        public void Dispose()
        {
            if (this.checkTimer != null)
            {
                this.StopWatching();

                this.isWatching = false;
                this.checkTimer.Elapsed -= this.CheckTimer_Tick;
                this.checkTimer.Dispose();
                this.checkTimer = null;
            }
        }

        private void CheckTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                this.checkTimer?.Stop();

                this.Grabber.TryGrabAndWrite();

                this.checkTimer?.Start();
            }
            catch
            {
                // ignore
            }
        }
    }
}
