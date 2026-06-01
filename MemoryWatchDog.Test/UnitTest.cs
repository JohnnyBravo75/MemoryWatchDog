namespace MemoryWatchDog.Test
{
    using System;
    using System.IO;
    using Xunit;

    public class MemoryWatchDogTests
    {
        private void WaitForWatching(MemoryWatchDog memWatchDog, int seconds)
        {
            var startDate = DateTime.Now;
            while (memWatchDog.IsWatching)
            {
                Thread.Sleep(100);
                if ((DateTime.Now - startDate).TotalSeconds > seconds)
                {
                    memWatchDog.StopWatching();
                    return;
                }
            }
        }

        [Fact]
        public void Test_GrabOnce_SaveToFile()
        {
            var memWatchDog = new MemoryWatchDog();
            var memStats = memWatchDog.Grabber.GetMemoryStats();
            var filePath = MemoryStats.GetDefaultFilePath();
            memStats.WriteToFile(filePath);

            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public void Test_GrabPeriodically_SaveToFile()
        {


            var memWatchDog = new MemoryWatchDog();
            memWatchDog.Grabber.MinMemoryCleanupLimitBytes = 1000;
            memWatchDog.Grabber.WriteMemStatsFile = true;
            memWatchDog.Grabber.MemStatsFilter = new MemoryStatsFilter
            {
                AggregateObjects = true,
                MinObjectCount = 10,
                ExcludeNameSpaces = new List<string> { "System.", "Microsoft." }
            };

            memWatchDog.StartWatching(new TimeSpan(0, 0, 1));
            WaitForWatching(memWatchDog, seconds: 10);

            Assert.True(File.Exists(MemoryStats.GetDefaultFilePath()));
        }



        [Fact]
        public void Test_GrabOnce_CancellationToken()
        {
            try
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(1));

                var memWatchDog = new MemoryWatchDog();
                var stats = memWatchDog.Grabber.GetMemoryStats(cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(true);
            }
        }
    }
}