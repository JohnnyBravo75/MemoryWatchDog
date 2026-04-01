namespace MemoryWatchDog
{
    using System;

    public class LiveMemorySnapshot
    {
        public DateTime Timestamp { get; set; }

        public long WorkingSet { get; set; }

        public long PrivateBytes { get; set; }

        public long GCHeapSize { get; set; }

        public long Gen0Size { get; set; }

        public long Gen1Size { get; set; }

        public long Gen2Size { get; set; }

        public long LOHSize { get; set; }

        public long POHSize { get; set; }
    }
}
