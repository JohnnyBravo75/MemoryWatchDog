namespace MemoryWatchDog
{
    using System;

    public class MemoryStatsTakenEventArgs : EventArgs
    {
        public MemoryStats MemoryStats { get; }

        public MemoryStatsTakenEventArgs(MemoryStats memoryStats)
        {
            this.MemoryStats = memoryStats;
        }
    }
}
