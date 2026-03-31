namespace MemoryWatchDog.Test
{
    using System;
    using System.IO;
    using Xunit;

    public class MemoryWatchDogTests
    {
        [Fact]
        public void Test1()
        {
            var startDate = DateTime.Now;
            var memWatchDog = new MemoryWatchDog();
            var memStats = memWatchDog.GetMemoryStats();
            var filePath = memStats.GetDefaultFilePath();
            memStats.WriteToFile(filePath);
        }
    }
}