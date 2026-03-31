namespace MemoryWatchDog.Test
{
    using System;
    using System.IO;
    using Xunit;

    public class MemoryWatchDogTests
    {
        [Fact]
        public void Test_GrabOnce_SaveToFile()
        {
            var memWatchDog = new MemoryWatchDog();
            var memStats = memWatchDog.GetMemoryStats();
            var filePath = MemoryStats.GetDefaultFilePath();
            memStats.WriteToFile(filePath);

            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public void Test_GrabPeriodically_SaveToFile()
        {
            var startDate = DateTime.Now;

            var memWatchDog = new MemoryWatchDog
            {
                MinMemoryCleanupLimitBytes = 1000,
                WriteMemStatsFile = true,
                MemStatsFilter = new MemoryStatsFilter
                {
                    AggregateObjects = true,
                    MinObjectCount = 10,
                    ExcludeNameSpaces = new List<string> { "System.", "Microsoft." }
                }
            };

            memWatchDog.StartWatching(new TimeSpan(0, 0, 1));

            while (memWatchDog.IsWatching)
            {
                Thread.Sleep(100);
                if ((DateTime.Now - startDate).TotalSeconds > 15)
                {
                    memWatchDog.StopWatching();
                }
            }

            Assert.True(File.Exists(MemoryStats.GetDefaultFilePath()));
        }
    }
}