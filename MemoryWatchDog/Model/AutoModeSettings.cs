namespace MemoryWatchDog
{
    public class AutoModeSettings
    {
        /// <summary>
        /// Interval in seconds between lightweight snapshots.
        /// </summary>
        public int SnapshotIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// Maximum number of snapshots to keep in the rolling window.
        /// </summary>
        public int MaxSnapshotsToKeep { get; set; } = 20;

        /// <summary>
        /// Number of initial snapshots to collect before starting analysis (app warmup).
        /// </summary>
        public int WarmupSnapshotCount { get; set; } = 5;

        /// <summary>
        /// Minimum number of consecutive snapshots where a type must grow to be flagged.
        /// </summary>
        public int MinConsecutiveGrowthCount { get; set; } = 5;

        /// <summary>
        /// Minimum percentage growth per interval to count as "growing".
        /// </summary>
        public double MinGrowthRatePercent { get; set; } = 5.0;

        /// <summary>
        /// Number of intervals to wait after taking a targeted snapshot before re-analyzing.
        /// </summary>
        public int CooldownIntervalsAfterCapture { get; set; } = 3;
    }
}
