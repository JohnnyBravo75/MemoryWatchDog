namespace MemoryWatchDog.Core
{
    using System.Collections.Generic;

    /// <summary>
    /// Compact JSON-serializable context for LLM-based memory-leak analysis.
    /// Copy-to-clipboard from the Retention Graph tab in ObjectDetailWindow.
    /// </summary>
    public class LlmLeakContextDto
    {
        /// <summary>Fully qualified type name of the suspected leak.</summary>
        public string TypeName { get; set; } = "";

        /// <summary>Leak confidence level (Low / Medium / High / Confirmed).</summary>
        public string Confidence { get; set; } = "";

        /// <summary>Detected leak pattern(s) (e.g. ClassicLeak, SizeGrowth).</summary>
        public string Pattern { get; set; } = "";

        /// <summary>Object count in the first analyzed snapshot.</summary>
        public long InitialCount { get; set; }

        /// <summary>Object count in the most recent snapshot.</summary>
        public long CurrentCount { get; set; }

        /// <summary>Absolute count growth (CurrentCount - InitialCount).</summary>
        public long CountGrowth { get; set; }

        /// <summary>Total size in bytes in the first snapshot.</summary>
        public long InitialTotalSizeBytes { get; set; }

        /// <summary>Total size in bytes in the most recent snapshot.</summary>
        public long CurrentTotalSizeBytes { get; set; }

        /// <summary>Average size per object in the most recent snapshot (bytes).</summary>
        public double CurrentAverageSizeBytes { get; set; }

        /// <summary>R² from linear regression on count series (1.0 = perfect linear growth).</summary>
        public double TrendRSquared { get; set; }

        /// <summary>Slope from linear regression (positive = growing).</summary>
        public double TrendSlope { get; set; }

        /// <summary>Average count increase per snapshot interval.</summary>
        public double GrowthRatePerInterval { get; set; }

        /// <summary>Number of consecutive intervals with growth.</summary>
        public int ConsecutiveGrowthCount { get; set; }

        /// <summary>Whether disposed instances are still retained in memory.</summary>
        public bool HasDisposedInstances { get; set; }

        /// <summary>Estimated time until out-of-memory (human-readable).</summary>
        public string EstimatedTimeToOOM { get; set; } = "";

        /// <summary>
        /// Retention graph of the selected object instance (max. 4 levels deep).
        /// Shows what is keeping this object alive.
        /// </summary>
        public LlmRetentionNodeDto RetentionGraph { get; set; }
    }

    /// <summary>
    /// One node in the retention graph tree (max. 4 levels deep).
    /// </summary>
    public class LlmRetentionNodeDto
    {
        /// <summary>Fully qualified type name.</summary>
        public string TypeName { get; set; } = "";

        /// <summary>Field or property name through which this object is referenced (empty for root).</summary>
        public string FieldName { get; set; } = "";

        /// <summary>Object size in bytes.</summary>
        public ulong SizeBytes { get; set; }

        /// <summary>Child references (objects this object holds).</summary>
        public List<LlmRetentionNodeDto> Children { get; set; }
    }
}
