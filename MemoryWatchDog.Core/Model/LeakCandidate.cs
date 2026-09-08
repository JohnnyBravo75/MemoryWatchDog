namespace MemoryWatchDog.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Confidence level for a leak candidate.
    /// </summary>
    public enum LeakConfidence
    {
        Low,
        Medium,
        High,
        Confirmed
    }

    /// <summary>
    /// Classification of the detected leak pattern (#6).
    /// </summary>
    public enum LeakPattern
    {
        /// <summary>Count grows linearly, size grows linearly — objects created and never released.</summary>
        ClassicLeak,

        /// <summary>Size grows while count remains stable — individual objects growing unboundedly.</summary>
        SizeGrowth,

        /// <summary>Disposed objects still retained in memory.</summary>
        DisposedButRetained,

        /// <summary>Combination of growth + disposed detection.</summary>
        GrowthAndDisposed,

        /// <summary>Count growth correlates with Gen2 heap growth.</summary>
        Gen2Correlated
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
        /// R² value from linear regression on the count series (#1).
        /// 1.0 = perfect linear growth, 0.0 = no correlation.
        /// </summary>
        public double TrendRSquared { get; set; }

        /// <summary>
        /// Slope from linear regression on the count series (#1).
        /// Positive = growing, negative = shrinking.
        /// </summary>
        public double TrendSlope { get; set; }

        /// <summary>
        /// Average object size in the most recent snapshot (#5).
        /// </summary>
        public double CurrentAverageSize { get; set; }

        /// <summary>
        /// Average object size in the earliest analyzed snapshot (#5).
        /// </summary>
        public double InitialAverageSize { get; set; }

        /// <summary>
        /// Whether the type also has disposed-but-retained instances (#4).
        /// </summary>
        public bool HasDisposedInstances { get; set; }

        /// <summary>
        /// The classified leak pattern (#6).
        /// </summary>
        public LeakPattern Pattern { get; set; } = LeakPattern.ClassicLeak;

        /// <summary>
        /// Estimated time until out-of-memory based on current growth rate (#7).
        /// Null if not calculable.
        /// </summary>
        public TimeSpan? EstimatedTimeToOOM { get; set; }

        /// <summary>
        /// Number of analysis cycles where this candidate was not growing (#8).
        /// Used for automatic demotion.
        /// </summary>
        public int StableIntervalCount { get; set; }

        /// <summary>
        /// Whether this candidate was previously removed and has reappeared (#8).
        /// </summary>
        public bool IsRecurring { get; set; }

        /// <summary>
        /// Detected leak patterns as human-readable list (#6).
        /// </summary>
        public List<LeakPattern> DetectedPatterns { get; set; } = new List<LeakPattern>();

        /// <summary>
        /// Human-readable confidence text for UI binding.
        /// </summary>
        public string ConfidenceText
        {
            get
            {
                switch (this.Confidence)
                {
                    case LeakConfidence.Confirmed: return "⛔ Confirmed";
                    case LeakConfidence.High: return "🔴 High";
                    case LeakConfidence.Medium: return "🟡 Medium";
                    default: return "🟢 Low";
                }
            }
        }

        /// <summary>
        /// Human-readable pattern text for UI binding (#6).
        /// </summary>
        public string PatternText
        {
            get
            {
                if (this.DetectedPatterns == null || this.DetectedPatterns.Count == 0)
                {
                    return FormatPattern(this.Pattern);
                }

                var parts = new List<string>();
                foreach (var p in this.DetectedPatterns)
                {
                    parts.Add(FormatPattern(p));
                }
                return string.Join(", ", parts);
            }
        }

        /// <summary>
        /// Estimated time to OOM as human-readable string (#7).
        /// </summary>
        public string EstimatedTimeToOOMText
        {
            get
            {
                if (this.EstimatedTimeToOOM == null)
                {
                    return "—";
                }

                var t = this.EstimatedTimeToOOM.Value;
                if (t.TotalDays >= 1)
                {
                    return $"~{t.TotalDays:0.#} days";
                }
                if (t.TotalHours >= 1)
                {
                    return $"~{t.TotalHours:0.#} hours";
                }
                return $"~{t.TotalMinutes:0} min";
            }
        }

        /// <summary>
        /// Trend formatted for UI as an arrow with label.
        /// </summary>
        public string TrendText
        {
            get
            {
                if (this.TrendSlope <= -0.5)
                {
                    return "↓ Dropping";
                }
                if (this.TrendSlope < 0.5)
                {
                    return "→ Stable";
                }
                if (this.TrendRSquared >= 0.8)
                {
                    return "⬆ Strong";
                }
                if (this.TrendRSquared >= 0.5)
                {
                    return "↗ Growing";
                }
                return "↗ Weak";
            }
        }

        /// <summary>
        /// Average size growth formatted for UI (#5).
        /// </summary>
        public double AverageSizeGrowth
        {
            get { return this.CurrentAverageSize - this.InitialAverageSize; }
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
            return $"{TypeName}: {InitialCount} → {CurrentCount} (+{CountGrowth}), {Confidence} confidence, {Pattern}";
        }

        private static string FormatPattern(LeakPattern pattern)
        {
            switch (pattern)
            {
                case LeakPattern.ClassicLeak: return "Classic Leak";
                case LeakPattern.SizeGrowth: return "Size Growth";
                case LeakPattern.DisposedButRetained: return "Disposed+Retained";
                case LeakPattern.GrowthAndDisposed: return "Growth+Disposed";
                case LeakPattern.Gen2Correlated: return "Gen2 Correlated";
                default: return pattern.ToString();
            }
        }
    }
}
