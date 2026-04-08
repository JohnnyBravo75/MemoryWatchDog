namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Analyzes a series of aggregated memory snapshots to detect types with sustained growth patterns
    /// that indicate potential memory leaks.
    /// </summary>
    public class LeakDetector
    {
        /// <summary>
        /// Analyzes the given snapshots and returns a list of leak candidates.
        /// Snapshots must be ordered by capture date (oldest first).
        /// </summary>
        public List<LeakCandidate> Analyze(List<MemoryStats> snapshots, AutoModeSettings settings)
        {
            if (snapshots == null || snapshots.Count < settings.WarmupSnapshotCount)
            {
                return new List<LeakCandidate>();
            }

            // Collect all type names across all snapshots
            var allTypeNames = new HashSet<string>();
            foreach (var snapshot in snapshots)
            {
                foreach (var typeName in snapshot.Types.Keys)
                {
                    allTypeNames.Add(typeName);
                }
            }

            var candidates = new List<LeakCandidate>();

            foreach (var typeName in allTypeNames)
            {
                var candidate = AnalyzeType(typeName, snapshots, settings);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            // Sort by confidence (High first), then by count growth descending
            candidates.Sort((a, b) =>
            {
                int cmp = b.Confidence.CompareTo(a.Confidence);
                if (cmp != 0) return cmp;
                return b.CountGrowth.CompareTo(a.CountGrowth);
            });

            return candidates;
        }

        private LeakCandidate AnalyzeType(string typeName, List<MemoryStats> snapshots, AutoModeSettings settings)
        {
            // Build the count series for this type across all snapshots
            var countSeries = new List<long>();
            var sizeSeries = new List<long>();
            var dateSeries = new List<DateTime>();

            foreach (var snapshot in snapshots)
            {
                if (snapshot.Types.TryGetValue(typeName, out var typeInfo))
                {
                    countSeries.Add(typeInfo.Count);
                    sizeSeries.Add((long)typeInfo.Size);
                }
                else
                {
                    countSeries.Add(0);
                    sizeSeries.Add(0);
                }

                dateSeries.Add(snapshot.CaptureDate);
            }

            if (countSeries.Count < 2)
            {
                return null;
            }

            // Count consecutive growths from the end going backwards
            int consecutiveGrowths = 0;
            for (int i = countSeries.Count - 1; i > 0; i--)
            {
                if (countSeries[i] > countSeries[i - 1])
                {
                    consecutiveGrowths++;
                }
                else
                {
                    break;
                }
            }

            if (consecutiveGrowths < settings.MinConsecutiveGrowthCount)
            {
                return null;
            }

            // Calculate growth rate
            int growthStartIndex = countSeries.Count - 1 - consecutiveGrowths;
            long startCount = countSeries[growthStartIndex];
            long endCount = countSeries[countSeries.Count - 1];

            if (startCount <= 0)
            {
                startCount = 1; // avoid division by zero
            }

            double growthPercent = ((double)(endCount - startCount) / startCount) * 100.0;
            if (growthPercent < settings.MinGrowthRatePercent)
            {
                return null;
            }

            double growthRatePerInterval = (double)(endCount - countSeries[growthStartIndex]) / consecutiveGrowths;

            // Determine confidence
            LeakConfidence confidence;
            if (consecutiveGrowths >= settings.MinConsecutiveGrowthCount * 2 && growthPercent > 50)
            {
                confidence = LeakConfidence.High;
            }
            else if (consecutiveGrowths >= settings.MinConsecutiveGrowthCount + 2 || growthPercent > 25)
            {
                confidence = LeakConfidence.Medium;
            }
            else
            {
                confidence = LeakConfidence.Low;
            }

            // Boost confidence if size growth is significant (> 1MB)
            long sizeGrowth = sizeSeries[sizeSeries.Count - 1] - sizeSeries[growthStartIndex];
            if (sizeGrowth > 1024 * 1024 && confidence < LeakConfidence.High)
            {
                confidence = (LeakConfidence)((int)confidence + 1);
            }

            return new LeakCandidate
            {
                TypeName = typeName,
                InitialCount = countSeries[growthStartIndex],
                CurrentCount = endCount,
                ConsecutiveGrowthCount = consecutiveGrowths,
                GrowthRatePerInterval = growthRatePerInterval,
                InitialTotalSize = sizeSeries[growthStartIndex],
                CurrentTotalSize = sizeSeries[sizeSeries.Count - 1],
                Confidence = confidence,
                FirstSeen = dateSeries[growthStartIndex],
                LastSeen = dateSeries[dateSeries.Count - 1]
            };
        }

        /// <summary>
        /// Prunes the snapshot list to keep at most maxToKeep snapshots.
        /// Keeps the first, last, and evenly-spaced middle snapshots to preserve history.
        /// </summary>
        public static void PruneSnapshots(List<MemoryStats> snapshots, int maxToKeep)
        {
            if (snapshots == null || snapshots.Count <= maxToKeep || maxToKeep < 3)
            {
                return;
            }

            int toRemove = snapshots.Count - maxToKeep;

            // Always keep first and last. Remove from the middle, preferring to keep evenly spaced ones.
            // Strategy: keep indices that are evenly distributed
            var keepIndices = new HashSet<int>();
            keepIndices.Add(0);
            keepIndices.Add(snapshots.Count - 1);

            int middleSlots = maxToKeep - 2;
            if (middleSlots > 0)
            {
                double step = (double)(snapshots.Count - 2) / (middleSlots);
                for (int i = 0; i < middleSlots; i++)
                {
                    int idx = 1 + (int)Math.Round(i * step);
                    if (idx >= snapshots.Count - 1)
                    {
                        idx = snapshots.Count - 2;
                    }

                    keepIndices.Add(idx);
                }
            }

            // Remove snapshots not in keepIndices (iterate backwards to preserve indices)
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (!keepIndices.Contains(i))
                {
                    var removed = snapshots[i];
                    removed.Clear();
                    snapshots.RemoveAt(i);
                }
            }
        }
    }
}
