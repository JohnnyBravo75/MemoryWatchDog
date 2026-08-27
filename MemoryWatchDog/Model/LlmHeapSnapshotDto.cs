namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Compact JSON-serializable heap snapshot for LLM-based memory analysis.
    /// Contains system-level metrics and the top heap types sliced by three dimensions.
    /// </summary>
    public class LlmHeapSnapshotDto
    {
        // ── Process & capture info ────────────────────────────────────────────

        public DateTime CaptureDate { get; set; }
        public string ProcessName { get; set; } = "";
        public int ProcessId { get; set; }
        public string NETVersion { get; set; } = "";

        // ── Memory metrics ────────────────────────────────────────────────────

        /// <summary>Working set of the process in bytes.</summary>
        public long WorkingSetBytes { get; set; }

        /// <summary>Total GC heap size in bytes.</summary>
        public long GCHeapBytes { get; set; }

        public long Gen0Bytes { get; set; }
        public long Gen1Bytes { get; set; }
        public long Gen2Bytes { get; set; }

        /// <summary>Large Object Heap size in bytes.</summary>
        public long LOHBytes { get; set; }

        /// <summary>Pinned Object Heap size in bytes.</summary>
        public long POHBytes { get; set; }

        /// <summary>Total size of all collected heap objects in bytes.</summary>
        public long TotalCollectedObjectBytes { get; set; }

        /// <summary>Total distinct object types found on the heap.</summary>
        public int UniqueTypeCount { get; set; }

        /// <summary>Total object count across all types.</summary>
        public long TotalObjectCount { get; set; }

        /// <summary>
        /// True when the snapshot was taken in aggregate mode.
        /// In that case SampleObjects in each type entry will be empty.
        /// </summary>
        public bool IsAggregateMode { get; set; }

        // ── Top-type slices ───────────────────────────────────────────────────

        /// <summary>
        /// Top N types by total memory consumption (TotalSize desc).
        /// Most relevant for "what is eating the most memory?"
        /// </summary>
        public List<LlmHeapTypeDto> TopByTotalSize { get; set; } = new List<LlmHeapTypeDto>();

        /// <summary>
        /// Top N types by instance count (Count desc).
        /// Most relevant for "what is accumulating the most objects?"
        /// </summary>
        public List<LlmHeapTypeDto> TopByCount { get; set; } = new List<LlmHeapTypeDto>();

        /// <summary>
        /// Top N types by average size per instance (AvgSize desc).
        /// Most relevant for "what individual objects are abnormally large?"
        /// </summary>
        public List<LlmHeapTypeDto> TopByAvgSize { get; set; } = new List<LlmHeapTypeDto>();
    }

    /// <summary>
    /// A single type entry in a heap snapshot top-list.
    /// </summary>
    public class LlmHeapTypeDto
    {
        /// <summary>Fully qualified type name.</summary>
        public string TypeName { get; set; } = "";

        /// <summary>Instance count on the heap.</summary>
        public long Count { get; set; }

        /// <summary>Total size of all instances in bytes.</summary>
        public ulong TotalSizeBytes { get; set; }

        /// <summary>Average size per instance in bytes.</summary>
        public double AvgSizeBytes { get; set; }

        /// <summary>Share of the total collected heap size (0–100 %).</summary>
        public double PercentOfHeap { get; set; }

        /// <summary>
        /// Up to 10 sampled object retention graphs for this type.
        /// Empty when the snapshot was taken in aggregate mode.
        /// </summary>
        public List<LlmRetentionNodeDto> SampleObjects { get; set; } = new List<LlmRetentionNodeDto>();
    }
}
