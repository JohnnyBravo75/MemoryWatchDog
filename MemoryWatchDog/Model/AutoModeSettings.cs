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

        /// <summary>
        /// Force GC on the target process before taking each snapshot to reduce false positives (#3).
        /// </summary>
        public bool ForceGCBeforeSnapshot { get; set; } = true;

        /// <summary>
        /// Minimum R² from linear regression to accept a trend as a leak signal (#1).
        /// </summary>
        public double MinTrendRSquared { get; set; } = 0.4;

        /// <summary>
        /// Number of stable (non-growing) intervals before a candidate is demoted (#8).
        /// </summary>
        public int DemotionStableIntervals { get; set; } = 3;

        /// <summary>
        /// Minimum absolute object count growth (current minus baseline minimum) to flag a type.
        /// Types that only grew by a handful of objects are noise, not leaks.
        /// </summary>
        public int MinAbsoluteGrowth { get; set; } = 10;

        /// <summary>
        /// If the current count is within this percentage of the baseline minimum,
        /// the type has recovered and should not be flagged.
        /// 0.1 = 10% tolerance above baseline.
        /// </summary>
        public double RecoveryTolerancePercent { get; set; } = 0.1;
    }
}
