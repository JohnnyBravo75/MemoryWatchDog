namespace MemoryWatchDog.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json;

    /// <summary>
    /// Structured report generated after an auto-watch session (#9).
    /// </summary>
    public class LeakReport
    {
        public DateTime SessionStart { get; set; }

        public DateTime SessionEnd { get; set; }

        public string ProcessName { get; set; } = "";

        public int ProcessId { get; set; }

        public int TotalSnapshotsTaken { get; set; }

        public TimeSpan SessionDuration
        {
            get { return this.SessionEnd - this.SessionStart; }
        }

        public string SessionDurationText
        {
            get
            {
                var d = this.SessionDuration;
                if (d.TotalHours >= 1)
                {
                    return $"{(int)d.TotalHours}h {d.Minutes}m {d.Seconds}s";
                }
                return $"{d.Minutes}m {d.Seconds}s";
            }
        }

        /// <summary>
        /// All detected leak candidates at the time the report was generated.
        /// </summary>
        public List<LeakCandidate> LeakCandidates { get; set; } = new List<LeakCandidate>();

        /// <summary>
        /// System-level memory trend data: Working Set over time.
        /// </summary>
        public List<MemoryTrendPoint> WorkingSetTrend { get; set; } = new List<MemoryTrendPoint>();

        /// <summary>
        /// System-level memory trend data: Gen2 size over time.
        /// </summary>
        public List<MemoryTrendPoint> Gen2Trend { get; set; } = new List<MemoryTrendPoint>();

        /// <summary>
        /// System-level memory trend data: GC Heap size over time.
        /// </summary>
        public List<MemoryTrendPoint> GCHeapTrend { get; set; } = new List<MemoryTrendPoint>();

        /// <summary>
        /// Settings used during the session.
        /// </summary>
        public AutoModeSettings Settings { get; set; }

        /// <summary>
        /// Builds a text summary of the report.
        /// </summary>
        public string BuildSummary()
        {
            var lines = new List<string>();
            lines.Add("═══════════════════════════════════════════════════════════════");
            lines.Add("  MemoryWatchDog — Leak Detection Report");
            lines.Add("═══════════════════════════════════════════════════════════════");
            lines.Add("");
            lines.Add($"  Process:           {this.ProcessName} (PID {this.ProcessId})");
            lines.Add($"  Session Start:     {this.SessionStart:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"  Session End:       {this.SessionEnd:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"  Duration:          {this.SessionDurationText}");
            lines.Add($"  Snapshots Taken:   {this.TotalSnapshotsTaken}");
            lines.Add($"  Interval:          {this.Settings?.SnapshotIntervalSeconds ?? 0}s");
            lines.Add("");

            if (this.WorkingSetTrend.Count >= 2)
            {
                var first = this.WorkingSetTrend[0];
                var last = this.WorkingSetTrend[this.WorkingSetTrend.Count - 1];
                lines.Add($"  Working Set:       {CommonUtil.FormatBytes(first.Value)} → {CommonUtil.FormatBytes(last.Value)}");
            }

            if (this.GCHeapTrend.Count >= 2)
            {
                var first = this.GCHeapTrend[0];
                var last = this.GCHeapTrend[this.GCHeapTrend.Count - 1];
                lines.Add($"  GC Heap:           {CommonUtil.FormatBytes(first.Value)} → {CommonUtil.FormatBytes(last.Value)}");
            }

            if (this.Gen2Trend.Count >= 2)
            {
                var first = this.Gen2Trend[0];
                var last = this.Gen2Trend[this.Gen2Trend.Count - 1];
                lines.Add($"  Gen 2:             {CommonUtil.FormatBytes(first.Value)} → {CommonUtil.FormatBytes(last.Value)}");
            }

            lines.Add("");
            lines.Add("───────────────────────────────────────────────────────────────");
            lines.Add($"  Leak Candidates:   {this.LeakCandidates.Count}");
            lines.Add("───────────────────────────────────────────────────────────────");

            if (this.LeakCandidates.Count == 0)
            {
                lines.Add("  No leak candidates detected.");
            }
            else
            {
                foreach (var c in this.LeakCandidates)
                {
                    lines.Add("");
                    lines.Add($"  [{c.ConfidenceText}] {c.TypeName}");
                    lines.Add($"    Pattern:          {c.PatternText}");
                    lines.Add($"    Count:            {c.InitialCount} → {c.CurrentCount} (+{c.CountGrowth})");
                    lines.Add($"    Size:             {CommonUtil.FormatBytes(c.InitialTotalSize)} → {CommonUtil.FormatBytes(c.CurrentTotalSize)} (+{CommonUtil.FormatBytes(c.SizeGrowth)})");
                    lines.Add($"    Growth/Interval:  +{c.GrowthRatePerInterval:0.0}");
                    lines.Add($"    Trend:            R²={c.TrendRSquared:0.000}, slope={c.TrendSlope:0.0}");
                    lines.Add($"    Avg Size:         {c.InitialAverageSize:0} → {c.CurrentAverageSize:0}");
                    lines.Add($"    Consecutive:      {c.ConsecutiveGrowthCount} intervals");
                    lines.Add($"    Disposed:         {(c.HasDisposedInstances ? "Yes — disposed but still in memory" : "No")}");
                    lines.Add($"    Est. Time to OOM: {c.EstimatedTimeToOOMText}");
                    if (c.IsRecurring)
                    {
                        lines.Add($"    ⚠ Recurring:     This leak was previously stable but reappeared.");
                    }
                }
            }

            lines.Add("");
            lines.Add("═══════════════════════════════════════════════════════════════");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Builds the report from the current auto-watch session data.
        /// </summary>
        public static LeakReport Build(
            List<MemoryStats> snapshots,
            List<LeakCandidate> candidates,
            AutoModeSettings settings,
            int totalSnapshotsTaken)
        {
            var report = new LeakReport
            {
                Settings = settings,
                TotalSnapshotsTaken = totalSnapshotsTaken,
                LeakCandidates = candidates ?? new List<LeakCandidate>()
            };

            if (snapshots != null && snapshots.Count > 0)
            {
                report.SessionStart = snapshots[0].CaptureDate;
                report.SessionEnd = snapshots[snapshots.Count - 1].CaptureDate;
                report.ProcessName = snapshots[0].ProcessName ?? "";
                report.ProcessId = snapshots[0].ProcessId;

                foreach (var s in snapshots)
                {
                    report.WorkingSetTrend.Add(new MemoryTrendPoint(s.CaptureDate, s.WorkingSet));
                    report.Gen2Trend.Add(new MemoryTrendPoint(s.CaptureDate, s.Gen2Size));
                    report.GCHeapTrend.Add(new MemoryTrendPoint(s.CaptureDate, s.GCHeapSize));
                }
            }

            return report;
        }

        public void WriteToFile(string fileName)
        {
            using (StreamWriter file = File.CreateText(fileName))
            {
                var serializer = new JsonSerializer() { Formatting = Formatting.Indented };
                serializer.Serialize(file, this);
            }
        }
    }

    /// <summary>
    /// A single point in a memory trend series.
    /// </summary>
    public class MemoryTrendPoint
    {
        public DateTime Timestamp { get; set; }
        public long Value { get; set; }

        public MemoryTrendPoint() { }

        public MemoryTrendPoint(DateTime timestamp, long value)
        {
            this.Timestamp = timestamp;
            this.Value = value;
        }
    }
}
