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
        // Candidate history for demotion/promotion (#8)
        private readonly Dictionary<string, LeakCandidate> candidateHistory = new Dictionary<string, LeakCandidate>();
        private readonly HashSet<string> previouslyRemovedCandidates = new HashSet<string>();

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

            // Compute Gen2 growth for correlation analysis (#2)
            double gen2GrowthRate = ComputeGen2GrowthRate(snapshots);

            // Available memory for OOM estimation (#7)
            long availableMemoryBytes = EstimateAvailableMemory(snapshots);

            var currentCandidateNames = new HashSet<string>();
            var candidates = new List<LeakCandidate>();

            foreach (var typeName in allTypeNames)
            {
                var candidate = AnalyzeType(typeName, snapshots, settings, gen2GrowthRate, availableMemoryBytes);
                if (candidate != null)
                {
                    currentCandidateNames.Add(typeName);

                    // Demotion/promotion logic (#8)
                    ApplyCandidateHistory(candidate, settings);

                    // Remove candidates that have been stable for too long —
                    // they keep appearing technically but aren't actually growing
                    if (candidate.StableIntervalCount < settings.DemotionStableIntervals)
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            // Handle demotion for candidates that are no longer detected (#8)
            DemoteAbsentCandidates(currentCandidateNames, settings);

            // Sort by confidence (Confirmed/High first), then by count growth descending
            candidates.Sort((a, b) =>
            {
                int cmp = b.Confidence.CompareTo(a.Confidence);
                if (cmp != 0) return cmp;
                return b.CountGrowth.CompareTo(a.CountGrowth);
            });

            return candidates;
        }

        /// <summary>
        /// Marks types that have disposed-but-retained instances in a detailed snapshot (#4).
        /// Call this after taking a targeted detailed snapshot of suspect types.
        /// </summary>
        public void CrossReferenceDisposedObjects(List<LeakCandidate> candidates, MemoryStats detailedSnapshot)
        {
            if (candidates == null || detailedSnapshot == null)
            {
                return;
            }

            foreach (var candidate in candidates)
            {
                if (detailedSnapshot.Types.TryGetValue(candidate.TypeName, out var typeInfo))
                {
                    bool hasDisposed = false;
                    foreach (var obj in typeInfo.Objects)
                    {
                        if (obj.IsDisposed)
                        {
                            hasDisposed = true;
                            break;
                        }
                    }

                    if (hasDisposed)
                    {
                        candidate.HasDisposedInstances = true;

                        if (!candidate.DetectedPatterns.Contains(LeakPattern.GrowthAndDisposed))
                        {
                            candidate.DetectedPatterns.Add(LeakPattern.GrowthAndDisposed);
                        }

                        // Boost to Confirmed confidence (#4)
                        candidate.Confidence = LeakConfidence.Confirmed;
                    }
                }
            }
        }

        /// <summary>
        /// Resets candidate history. Call when starting a new watch session.
        /// </summary>
        public void Reset()
        {
            this.candidateHistory.Clear();
            this.previouslyRemovedCandidates.Clear();
        }

        private LeakCandidate AnalyzeType(
            string typeName,
            List<MemoryStats> snapshots,
            AutoModeSettings settings,
            double gen2GrowthRate,
            long availableMemoryBytes)
        {
            // Build the count and size series for this type across all snapshots
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

            // === Baseline: the minimum count observed in the window ===
            // A real leak never returns to its baseline. If current is near the min, it recovered.
            long baselineCount = countSeries[0];
            long baselineSize = sizeSeries[0];
            int baselineIndex = 0;
            long peakCount = countSeries[0];
            for (int i = 1; i < countSeries.Count; i++)
            {
                if (countSeries[i] < baselineCount)
                {
                    baselineCount = countSeries[i];
                    baselineSize = sizeSeries[i];
                    baselineIndex = i;
                }
                if (countSeries[i] > peakCount)
                {
                    peakCount = countSeries[i];
                }
            }

            long currentCount = countSeries[countSeries.Count - 1];
            long currentSize = sizeSeries[sizeSeries.Count - 1];
            long netGrowthFromBaseline = currentCount - baselineCount;
            long netSizeGrowthFromBaseline = currentSize - baselineSize;

            // === Quick dismiss: growth too small to matter ===
            // For count-based leaks: need MinAbsoluteGrowth objects above baseline.
            // For size-only leaks (count stable but size growing): need >20% size increase.
            bool hasSignificantCountGrowth = netGrowthFromBaseline >= settings.MinAbsoluteGrowth;
            bool hasSignificantSizeGrowth = baselineSize > 0
                && netSizeGrowthFromBaseline > baselineSize * 0.2
                && netGrowthFromBaseline >= 0;
            if (!hasSignificantCountGrowth && !hasSignificantSizeGrowth)
            {
                return null;
            }

            // === Quick dismiss: type recovered to near its baseline ===
            // Only applies to count-based detection. Size-only growth is checked separately.
            if (hasSignificantCountGrowth)
            {
                double recoveryThreshold = baselineCount > 0
                    ? baselineCount * (1.0 + settings.RecoveryTolerancePercent)
                    : settings.MinAbsoluteGrowth;
                if (currentCount <= recoveryThreshold)
                {
                    return null;
                }
            }

            // === Linear regression on count series (#1) ===
            double countSlope, countIntercept, countRSquared;
            ComputeLinearRegression(countSeries, out countSlope, out countIntercept, out countRSquared);

            // === Linear regression on size series (#5) ===
            double sizeSlope, sizeIntercept, sizeRSquared;
            ComputeLinearRegression(sizeSeries, out sizeSlope, out sizeIntercept, out sizeRSquared);

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

            // === Recent direction check ===
            // If any of the last 2 snapshots show a decline, or current dropped >20% from peak, dismiss.
            int recentWindow = Math.Min(3, countSeries.Count - 1);
            if (recentWindow >= 2)
            {
                int recentDeclines = 0;
                for (int i = countSeries.Count - 1; i > countSeries.Count - 1 - recentWindow; i--)
                {
                    if (countSeries[i] < countSeries[i - 1])
                    {
                        recentDeclines++;
                    }
                }

                // If majority of recent intervals are declining, dismiss this type
                if (recentDeclines >= recentWindow - 1)
                {
                    return null;
                }
            }

            // If current count dropped significantly from peak, type is recovering
            if (peakCount > 0 && currentCount < peakCount * 0.8)
            {
                return null;
            }

            // === Decision: use EITHER consecutive growth OR trend analysis (#1) ===
            bool hasConsecutiveGrowth = consecutiveGrowths >= settings.MinConsecutiveGrowthCount;
            bool hasStrongTrend = countSlope > 0 && countRSquared >= settings.MinTrendRSquared;
            // Size-only: count stable but size per instance grows — require strong evidence
            bool hasSizeOnlyGrowth = countSlope <= 0.5
                && sizeSlope > 0
                && sizeRSquared >= 0.8
                && baselineSize > 0
                && (double)netSizeGrowthFromBaseline / baselineSize > 0.5; // >50% total size increase

            if (!hasConsecutiveGrowth && !hasStrongTrend && !hasSizeOnlyGrowth)
            {
                return null;
            }

            // Calculate growth metrics — always measure from baseline (minimum), not first snapshot
            long startCount = baselineCount > 0 ? baselineCount : 1;
            long endCount = currentCount;
            int growthStartIndex = baselineIndex;

            double growthPercent = ((double)(endCount - startCount) / startCount) * 100.0;

            // For trend-based detection, relax the growth percent requirement
            if (hasConsecutiveGrowth && growthPercent < settings.MinGrowthRatePercent && !hasStrongTrend)
            {
                return null;
            }

            int effectiveIntervals = countSeries.Count - 1 - baselineIndex;
            double growthRatePerInterval = effectiveIntervals > 0
                ? (double)(endCount - startCount) / effectiveIntervals
                : 0;

            // === Size-per-instance tracking (#5) ===
            double initialAvgSize = startCount > 0 ? (double)baselineSize / startCount : 0;
            double currentAvgSize = endCount > 0 ? (double)sizeSeries[sizeSeries.Count - 1] / endCount : 0;

            // === Determine confidence ===
            LeakConfidence confidence = DetermineConfidence(
                consecutiveGrowths, growthPercent, countRSquared, sizeRSquared,
                sizeSeries, growthStartIndex, settings);

            // === GC Gen2 correlation (#2) ===
            bool gen2Correlated = false;
            if (gen2GrowthRate > 0 && countSlope > 0)
            {
                // Only correlate if both Gen2 and type count have meaningful growth
                // AND the type is still actively growing in recent snapshots
                double typeGrowthRate = countSlope;
                if (gen2GrowthRate > 0.5 && typeGrowthRate > 0.5 && consecutiveGrowths >= 2)
                {
                    gen2Correlated = true;
                    if (confidence < LeakConfidence.High)
                    {
                        confidence = (LeakConfidence)((int)confidence + 1);
                    }
                }
            }

            // === Classify leak pattern (#6) ===
            var patterns = new List<LeakPattern>();
            LeakPattern primaryPattern;

            if (hasSizeOnlyGrowth && !hasConsecutiveGrowth && !hasStrongTrend)
            {
                primaryPattern = LeakPattern.SizeGrowth;
                patterns.Add(LeakPattern.SizeGrowth);
            }
            else
            {
                primaryPattern = LeakPattern.ClassicLeak;
                patterns.Add(LeakPattern.ClassicLeak);
            }

            if (currentAvgSize > initialAvgSize * 1.2 && initialAvgSize > 0)
            {
                if (!patterns.Contains(LeakPattern.SizeGrowth))
                {
                    patterns.Add(LeakPattern.SizeGrowth);
                }
            }

            if (gen2Correlated)
            {
                patterns.Add(LeakPattern.Gen2Correlated);
            }

            // === Estimate time to OOM (#7) ===
            TimeSpan? estimatedTimeToOOM = null;
            if (sizeSlope > 0 && availableMemoryBytes > 0)
            {
                double intervalsToOOM = availableMemoryBytes / sizeSlope;
                if (intervalsToOOM > 0 && intervalsToOOM < 1000000)
                {
                    int intervalSeconds = snapshots.Count >= 2
                        ? (int)(snapshots[snapshots.Count - 1].CaptureDate - snapshots[0].CaptureDate).TotalSeconds / Math.Max(snapshots.Count - 1, 1)
                        : 60;
                    double secondsToOOM = intervalsToOOM * Math.Max(intervalSeconds, 1);
                    estimatedTimeToOOM = TimeSpan.FromSeconds(secondsToOOM);
                }
            }

            return new LeakCandidate
            {
                TypeName = typeName,
                InitialCount = baselineCount,
                CurrentCount = endCount,
                ConsecutiveGrowthCount = consecutiveGrowths,
                GrowthRatePerInterval = growthRatePerInterval,
                InitialTotalSize = baselineSize,
                CurrentTotalSize = sizeSeries[sizeSeries.Count - 1],
                Confidence = confidence,
                FirstSeen = dateSeries[growthStartIndex],
                LastSeen = dateSeries[dateSeries.Count - 1],
                TrendRSquared = countRSquared,
                TrendSlope = countSlope,
                InitialAverageSize = initialAvgSize,
                CurrentAverageSize = currentAvgSize,
                Pattern = primaryPattern,
                DetectedPatterns = patterns,
                EstimatedTimeToOOM = estimatedTimeToOOM
            };
        }

        private LeakConfidence DetermineConfidence(
            int consecutiveGrowths,
            double growthPercent,
            double countRSquared,
            double sizeRSquared,
            List<long> sizeSeries,
            int growthStartIndex,
            AutoModeSettings settings)
        {
            LeakConfidence confidence;

            // High: many consecutive growths + large percentage, or very strong trend
            if ((consecutiveGrowths >= settings.MinConsecutiveGrowthCount * 2 && growthPercent > 50)
                || countRSquared >= 0.9)
            {
                confidence = LeakConfidence.High;
            }
            else if ((consecutiveGrowths >= settings.MinConsecutiveGrowthCount + 2 || growthPercent > 25)
                     || countRSquared >= 0.7)
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

            return confidence;
        }

        // === Demotion/Promotion (#8) ===

        private void ApplyCandidateHistory(LeakCandidate candidate, AutoModeSettings settings)
        {
            string key = candidate.TypeName;

            if (this.candidateHistory.TryGetValue(key, out var previous))
            {
                // A candidate is actively growing only if its count increased since last analysis
                // AND it has recent consecutive growth. Using the full-window trend slope alone
                // is misleading because early growth dominates even after the type stabilizes.
                bool countIncreased = candidate.CurrentCount > previous.CurrentCount;
                bool hasRecentGrowth = candidate.ConsecutiveGrowthCount >= 2;

                if (countIncreased && hasRecentGrowth)
                {
                    candidate.StableIntervalCount = 0;
                }
                else
                {
                    // Carry forward and increment the stable count
                    candidate.StableIntervalCount = previous.StableIntervalCount + 1;
                }
            }

            // Check if this was previously removed and is now back
            if (this.previouslyRemovedCandidates.Contains(key))
            {
                candidate.IsRecurring = true;
                this.previouslyRemovedCandidates.Remove(key);

                // Recurring leaks get a confidence boost
                if (candidate.Confidence < LeakConfidence.High)
                {
                    candidate.Confidence = (LeakConfidence)((int)candidate.Confidence + 1);
                }
            }

            this.candidateHistory[key] = candidate;
        }

        private void DemoteAbsentCandidates(HashSet<string> currentCandidateNames, AutoModeSettings settings)
        {
            var toRemove = new List<string>();

            foreach (var kvp in this.candidateHistory)
            {
                if (!currentCandidateNames.Contains(kvp.Key))
                {
                    kvp.Value.StableIntervalCount++;

                    if (kvp.Value.StableIntervalCount >= settings.DemotionStableIntervals)
                    {
                        // Demote: remove from history, mark as previously removed
                        toRemove.Add(kvp.Key);
                        this.previouslyRemovedCandidates.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in toRemove)
            {
                this.candidateHistory.Remove(key);
            }
        }

        // === Linear Regression (#1) ===

        /// <summary>
        /// Computes simple linear regression (least squares) on a series of values.
        /// X values are the indices (0, 1, 2, ...).
        /// </summary>
        public static void ComputeLinearRegression(
            List<long> values,
            out double slope,
            out double intercept,
            out double rSquared)
        {
            slope = 0;
            intercept = 0;
            rSquared = 0;

            int n = values.Count;
            if (n < 2)
            {
                return;
            }

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;

            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = values[i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
                sumY2 += y * y;
            }

            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-10)
            {
                return;
            }

            slope = (n * sumXY - sumX * sumY) / denom;
            intercept = (sumY - slope * sumX) / n;

            // R² calculation
            double ssTot = sumY2 - (sumY * sumY) / n;
            double ssRes = 0;
            for (int i = 0; i < n; i++)
            {
                double predicted = intercept + slope * i;
                double residual = values[i] - predicted;
                ssRes += residual * residual;
            }

            rSquared = ssTot > 0 ? 1.0 - (ssRes / ssTot) : 0;
            if (rSquared < 0)
            {
                rSquared = 0;
            }
        }

        // === Gen2 Growth Rate (#2) ===

        private static double ComputeGen2GrowthRate(List<MemoryStats> snapshots)
        {
            if (snapshots.Count < 2)
            {
                return 0;
            }

            var gen2Values = new List<long>();
            foreach (var s in snapshots)
            {
                gen2Values.Add(s.Gen2Size);
            }

            double slope, intercept, rSquared;
            ComputeLinearRegression(gen2Values, out slope, out intercept, out rSquared);

            // Only return positive slope if the trend is meaningful
            return (slope > 0 && rSquared > 0.3) ? slope : 0;
        }

        // === OOM estimation helper (#7) ===

        private static long EstimateAvailableMemory(List<MemoryStats> snapshots)
        {
            if (snapshots.Count == 0)
            {
                return 0;
            }

            var latest = snapshots[snapshots.Count - 1];
            long currentUsage = latest.WorkingSet > 0 ? latest.WorkingSet : latest.GCHeapSize;

            // Assume 2GB limit for 32-bit or use a reasonable upper bound
            // In practice, this could be read from the system, but for estimation we use 4GB
            long assumedMax = 4L * 1024 * 1024 * 1024;

            long available = assumedMax - currentUsage;
            return available > 0 ? available : 0;
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

            // Always keep first and last. Remove from the middle, preferring to keep evenly spaced ones.
            var keepIndices = new HashSet<int>();
            keepIndices.Add(0);
            keepIndices.Add(snapshots.Count - 1);

            int middleSlots = maxToKeep - 2;
            if (middleSlots > 0)
            {
                double step = (double)(snapshots.Count - 2) / middleSlots;
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
