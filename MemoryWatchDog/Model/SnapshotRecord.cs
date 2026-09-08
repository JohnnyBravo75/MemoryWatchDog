namespace MemoryWatchDog
{
    using System;

    public class SnapshotRecord
    {
        public SnapshotRecord(MemoryStats stats, bool isAutoSnapshot = false)
        {
            this.Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            this.IsAutoSnapshot = isAutoSnapshot;
            this.DisplayDate = stats.CaptureDate.ToString("yyyy-MM-dd HH:mm:ss");
            this.DisplayProcess = $"{stats.ProcessName} (PID {stats.ProcessId})";
            this.ModeTag = isAutoSnapshot ? "Auto" : "Manual";
            this.ModeTagBrush = isAutoSnapshot ? "#C62828" : "#336699";
        }

        public MemoryStats Stats { get; set; }

        public string DisplayDate { get; }

        public string DisplayProcess { get; }

        public bool IsAutoSnapshot { get; }

        public string ModeTag { get; }

        public string ModeTagBrush { get; }
    }
}
