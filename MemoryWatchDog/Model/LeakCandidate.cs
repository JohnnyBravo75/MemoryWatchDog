namespace MemoryWatchDog
{
    using System;

    /// <summary>
    /// Confidence level for a leak candidate.
    /// </summary>
    public enum LeakConfidence
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Represents a type that is suspected of leaking memory based on growth analysis.
    /// </summary>
    public class LeakCandidate
    {
        /// <summary>
        /// The fully qualified type name.
        /// </summary>
        public string TypeName { get; set; } = "";

        /// <summary>
        /// Object count in the earliest analyzed snapshot.
        /// </summary>
        public long InitialCount { get; set; }

        /// <summary>
        /// Object count in the most recent snapshot.
        /// </summary>
        public long CurrentCount { get; set; }

        /// <summary>
        /// Number of consecutive snapshots (from the most recent going back) where the count increased.
        /// </summary>
        public int ConsecutiveGrowthCount { get; set; }

        /// <summary>
        /// Average count increase per snapshot interval.
        /// </summary>
        public double GrowthRatePerInterval { get; set; }

        /// <summary>
        /// Total size in the most recent snapshot.
        /// </summary>
        public long CurrentTotalSize { get; set; }

        /// <summary>
        /// Total size in the earliest analyzed snapshot.
        /// </summary>
        public long InitialTotalSize { get; set; }

        /// <summary>
        /// Confidence level of the leak detection.
        /// </summary>
        public LeakConfidence Confidence { get; set; }

        /// <summary>
        /// Timestamp when this type was first seen growing.
        /// </summary>
        public DateTime FirstSeen { get; set; }

        /// <summary>
        /// Timestamp of the most recent snapshot where growth was observed.
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// Human-readable confidence text for UI binding.
        /// </summary>
        public string ConfidenceText
        {
            get
            {
                switch (this.Confidence)
                {
                    case LeakConfidence.High: return "🔴 High";
                    case LeakConfidence.Medium: return "🟡 Medium";
                    default: return "🟢 Low";
                }
            }
        }

        /// <summary>
        /// Count difference between current and initial snapshot.
        /// </summary>
        public long CountGrowth
        {
            get { return this.CurrentCount - this.InitialCount; }
        }

        /// <summary>
        /// Size difference between current and initial snapshot.
        /// </summary>
        public long SizeGrowth
        {
            get { return this.CurrentTotalSize - this.InitialTotalSize; }
        }

        public override string ToString()
        {
            return $"{TypeName}: {InitialCount} → {CurrentCount} (+{CountGrowth}), {Confidence} confidence, {ConsecutiveGrowthCount} consecutive growths";
        }
    }
}
