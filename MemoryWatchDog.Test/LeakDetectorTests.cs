namespace MemoryWatchDog.Test
{
    public class LeakDetectorTests
    {
        private static MemoryStats CreateSnapshot(
            DateTime captureDate,
            long gen2Size = 0,
            long workingSet = 0,
            long gcHeapSize = 0,
            params (string typeName, long count, ulong size)[] types)
        {
            var stats = new MemoryStats
            {
                CaptureDate = captureDate,
                ProcessId = 1234,
                ProcessName = "TestProcess",
                Gen2Size = gen2Size,
                WorkingSet = workingSet,
                GCHeapSize = gcHeapSize
            };

            foreach (var (typeName, count, size) in types)
            {
                stats.Types[typeName] = new TypeInfo
                {
                    TypeName = typeName,
                    Count = count,
                    Size = size
                };
            }

            return stats;
        }

        // Helper overload for backward compatibility with existing tests
        private static MemoryStats CreateSnapshot(DateTime captureDate, params (string typeName, long count, ulong size)[] types)
        {
            return CreateSnapshot(captureDate, 0, 0, 0, types);
        }

        [Fact]
        public void Analyze_ReturnsEmpty_WhenFewerThanWarmupSnapshots()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings { WarmupSnapshotCount = 5, MinConsecutiveGrowthCount = 3 };

            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(DateTime.Now, ("MyApp.Foo", 10, 1000)),
                CreateSnapshot(DateTime.Now.AddMinutes(1), ("MyApp.Foo", 20, 2000)),
            };

            var result = detector.Analyze(snapshots, settings);

            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_DetectsGrowingType()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.LeakyService", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.LeakyService", 110, 11000)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.LeakyService", 120, 12000)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.LeakyService", 130, 13000)),
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.LeakyService", 140, 14000)),
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.LeakyService", 150, 15000)),
            };

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            Assert.Equal("MyApp.LeakyService", result[0].TypeName);
            Assert.Equal(5, result[0].ConsecutiveGrowthCount);
            Assert.Equal(100, result[0].InitialCount);
            Assert.Equal(150, result[0].CurrentCount);
            Assert.Equal(50, result[0].CountGrowth);
        }

        [Fact]
        public void Analyze_IgnoresStableTypes()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.StableService", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.StableService", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.StableService", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.StableService", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.StableService", 100, 10000)),
            };

            var result = detector.Analyze(snapshots, settings);

            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_IgnoresTransientSpike()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 4,
                MinGrowthRatePercent = 5.0,
                MinTrendRSquared = 0.7
            };

            var baseTime = DateTime.Now;
            // Type grows for 3 snapshots then drops — should NOT be flagged since we need 4 consecutive
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.TransientType", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.TransientType", 110, 11000)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.TransientType", 120, 12000)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.TransientType", 130, 13000)),
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.TransientType", 80, 8000)),  // drops
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.TransientType", 90, 9000)),
            };

            var result = detector.Analyze(snapshots, settings);

            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_DetectsMultipleLeaks_SortedByConfidence()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();

            // Create 10 snapshots where both types grow, but BigLeak grows more
            for (int i = 0; i < 10; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.BigLeak", 100 + i * 50, (ulong)(10000 + i * 200000)),   // large size growth
                    ("MyApp.SmallLeak", 100 + i * 5, (ulong)(1000 + i * 500))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Equal(2, result.Count);
            // BigLeak should be higher confidence due to size growth > 1MB
            Assert.Equal("MyApp.BigLeak", result[0].TypeName);
            Assert.True(result[0].Confidence >= result[1].Confidence);
        }

        [Fact]
        public void PruneSnapshots_KeepsFirstAndLast()
        {
            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 10; i++)
            {
                snapshots.Add(CreateSnapshot(baseTime.AddMinutes(i), ("MyApp.Foo", i * 10, (ulong)(i * 100))));
            }

            var firstDate = snapshots[0].CaptureDate;
            var lastDate = snapshots[9].CaptureDate;

            LeakDetector.PruneSnapshots(snapshots, 5);

            Assert.Equal(5, snapshots.Count);
            Assert.Equal(firstDate, snapshots[0].CaptureDate);
            Assert.Equal(lastDate, snapshots[snapshots.Count - 1].CaptureDate);
        }

        [Fact]
        public void PruneSnapshots_DoesNothingWhenUnderLimit()
        {
            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.Foo", 10, 100)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.Foo", 20, 200)),
            };

            LeakDetector.PruneSnapshots(snapshots, 5);

            Assert.Equal(2, snapshots.Count);
        }

        [Fact]
        public void Analyze_HandlesTypeAppearingMidStream()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime),                                              // type not present
                CreateSnapshot(baseTime.AddMinutes(1)),                                // type not present
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.NewType", 10, 1000)),   // appears
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.NewType", 20, 2000)),
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.NewType", 30, 3000)),
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.NewType", 40, 4000)),
                CreateSnapshot(baseTime.AddMinutes(6), ("MyApp.NewType", 50, 5000)),
            };

            var result = detector.Analyze(snapshots, settings);

            // The type grew from 0→10→20→30→40→50 across snapshots where it existed
            Assert.Single(result);
            Assert.Equal("MyApp.NewType", result[0].TypeName);
        }

        [Fact]
        public void Analyze_ConfidenceIsHighForLargeGrowth()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();

            // 12 consecutive growths with > 50% total growth + >1MB size growth
            for (int i = 0; i < 12; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.BigLeak", 100 + i * 100, (ulong)(100000 + i * 200000))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            Assert.True(result[0].Confidence >= LeakConfidence.High);
        }

        // ============ New tests for features #1-#9 ============

        [Fact]
        public void LinearRegression_PerfectLinearGrowth_RSquaredIsOne()
        {
            // Perfect y = 10 + 5x
            var values = new List<long> { 10, 15, 20, 25, 30, 35, 40 };

            LeakDetector.ComputeLinearRegression(values, out double slope, out double intercept, out double rSquared);

            Assert.Equal(5.0, slope, 1);
            Assert.Equal(10.0, intercept, 1);
            Assert.True(rSquared > 0.99, $"R² should be ~1.0, was {rSquared}");
        }

        [Fact]
        public void LinearRegression_ConstantValues_SlopeIsZero()
        {
            var values = new List<long> { 100, 100, 100, 100, 100 };

            LeakDetector.ComputeLinearRegression(values, out double slope, out double intercept, out double rSquared);

            Assert.True(Math.Abs(slope) < 0.01);
        }

        [Fact]
        public void LinearRegression_NoisyGrowth_RSquaredIsModerate()
        {
            // Noisy but overall growing: 100, 105, 98, 115, 110, 125, 120, 135
            var values = new List<long> { 100, 105, 98, 115, 110, 125, 120, 135 };

            LeakDetector.ComputeLinearRegression(values, out double slope, out double intercept, out double rSquared);

            Assert.True(slope > 0, "Slope should be positive");
            Assert.True(rSquared > 0.4 && rSquared < 1.0, $"R² should be moderate, was {rSquared}");
        }

        [Fact]
        public void Analyze_TrendBasedDetection_CatchesNoisyGrowth()
        {
            // #1: Type that grows overall but has a dip in the middle — consecutive check alone would miss it
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 5,  // requires 5 consecutive — the dip breaks this
                MinGrowthRatePercent = 5.0,
                MinTrendRSquared = 0.5
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.NoisyLeak", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.NoisyLeak", 115, 11500)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.NoisyLeak", 130, 13000)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.NoisyLeak", 125, 12500)),  // dip!
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.NoisyLeak", 145, 14500)),
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.NoisyLeak", 160, 16000)),
                CreateSnapshot(baseTime.AddMinutes(6), ("MyApp.NoisyLeak", 175, 17500)),
                CreateSnapshot(baseTime.AddMinutes(7), ("MyApp.NoisyLeak", 190, 19000)),
            };

            var result = detector.Analyze(snapshots, settings);

            // Should be detected via trend analysis even though consecutive growths < 5
            Assert.Single(result);
            Assert.Equal("MyApp.NoisyLeak", result[0].TypeName);
            Assert.True(result[0].TrendRSquared > 0.5);
            Assert.True(result[0].TrendSlope > 0);
        }

        [Fact]
        public void Analyze_PopulatesTrendMetrics()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 8; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.LinearLeak", 100 + i * 10, (ulong)(1000 + i * 100))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            var c = result[0];
            Assert.True(c.TrendSlope > 0, "Slope should be positive");
            Assert.True(c.TrendRSquared > 0.9, "R² should be high for perfect linear growth");
            Assert.Contains(LeakPattern.ClassicLeak, c.DetectedPatterns);
        }

        [Fact]
        public void Analyze_SizeGrowthDetection_CountStableSizeGrows()
        {
            // #5: Count stays at 1, but size keeps growing
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 10, // impossible via consecutive count alone
                MinGrowthRatePercent = 5.0,
                MinTrendRSquared = 0.5
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 8; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.GrowingBuffer", 1, (ulong)(10000 + i * 50000))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            Assert.Equal("MyApp.GrowingBuffer", result[0].TypeName);
            Assert.Contains(LeakPattern.SizeGrowth, result[0].DetectedPatterns);
            Assert.True(result[0].CurrentAverageSize > result[0].InitialAverageSize);
        }

        [Fact]
        public void Analyze_Gen2Correlation_BoostsConfidence()
        {
            // #2: Type grows alongside Gen2 heap growth
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 8; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    gen2Size: 1000000 + i * 500000,  // Gen2 growing steadily
                    workingSet: 50000000 + i * 1000000,
                    gcHeapSize: 10000000 + i * 1000000,
                    ("MyApp.Gen2Leak", 100 + i * 10, (ulong)(1000 + i * 100))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            Assert.Contains(LeakPattern.Gen2Correlated, result[0].DetectedPatterns);
        }

        [Fact]
        public void Analyze_EstimatesTimeToOOM()
        {
            // #7: Verify OOM estimation is populated
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 8; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    gen2Size: 0,
                    workingSet: 100000000 + i * 10000000, // ~100MB + 10MB/interval
                    gcHeapSize: 0,
                    ("MyApp.OOMCandidate", 100 + i * 20, (ulong)(1000000 + i * 500000))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            Assert.NotNull(result[0].EstimatedTimeToOOM);
            Assert.True(result[0].EstimatedTimeToOOM.Value.TotalMinutes > 0);
            Assert.False(string.IsNullOrEmpty(result[0].EstimatedTimeToOOMText));
            Assert.NotEqual("—", result[0].EstimatedTimeToOOMText);
        }

        [Fact]
        public void Analyze_CandidateDemotion_RemovesStableCandidates()
        {
            // #8: Candidate that stops growing should be demoted after DemotionStableIntervals
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0,
                DemotionStableIntervals = 2
            };

            var baseTime = DateTime.Now;

            // Phase 1: Type is growing — should be detected
            var growingSnapshots = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                growingSnapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.TempLeak", 100 + i * 10, (ulong)(1000 + i * 100))
                ));
            }

            var result1 = detector.Analyze(growingSnapshots, settings);
            Assert.Single(result1);

            // Phase 2: Type stabilizes — should be removed after DemotionStableIntervals
            var stableSnapshots = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                stableSnapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(10 + i),
                    ("MyApp.TempLeak", 200, 2000)
                ));
            }

            // First call with stable data
            var result2 = detector.Analyze(stableSnapshots, settings);
            // Second call — should trigger demotion
            var result3 = detector.Analyze(stableSnapshots, settings);
            // Third call — candidate should be demoted and remembered
            var result4 = detector.Analyze(stableSnapshots, settings);

            // The type should eventually disappear from results
            Assert.Empty(result4);
        }

        [Fact]
        public void Analyze_CandidatePromotion_RecurringLeak()
        {
            // #8: A previously demoted candidate that reappears should be marked as recurring
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0,
                DemotionStableIntervals = 1 // Quick demotion for test
            };

            var baseTime = DateTime.Now;

            // Phase 1: Growing
            var phase1 = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                phase1.Add(CreateSnapshot(baseTime.AddMinutes(i), ("MyApp.IntermittentLeak", 100 + i * 10, (ulong)(1000 + i * 100))));
            }
            detector.Analyze(phase1, settings);

            // Phase 2: Stable — causes demotion
            var phase2 = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                phase2.Add(CreateSnapshot(baseTime.AddMinutes(10 + i), ("MyApp.IntermittentLeak", 200, 2000)));
            }
            detector.Analyze(phase2, settings); // demotes
            detector.Analyze(phase2, settings); // removes

            // Phase 3: Growing again — should be marked recurring
            var phase3 = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                phase3.Add(CreateSnapshot(baseTime.AddMinutes(20 + i), ("MyApp.IntermittentLeak", 200 + i * 15, (ulong)(2000 + i * 150))));
            }
            var result = detector.Analyze(phase3, settings);

            Assert.Single(result);
            Assert.True(result[0].IsRecurring, "Candidate should be marked as recurring");
        }

        [Fact]
        public void CrossReferenceDisposedObjects_BoostsToConfirmed()
        {
            // #4: Cross-referencing with disposed objects should boost to Confirmed
            var detector = new LeakDetector();

            var candidates = new List<LeakCandidate>
            {
                new LeakCandidate
                {
                    TypeName = "MyApp.LeakyService",
                    Confidence = LeakConfidence.High,
                    DetectedPatterns = new List<LeakPattern> { LeakPattern.ClassicLeak }
                }
            };

            var detailedSnapshot = new MemoryStats();
            var typeInfo = new TypeInfo { TypeName = "MyApp.LeakyService" };
            typeInfo.AddObject(new ObjectInfo
            {
                TypeName = "MyApp.LeakyService",
                IsDisposed = true,
                Size = 1000
            });
            detailedSnapshot.Types["MyApp.LeakyService"] = typeInfo;

            detector.CrossReferenceDisposedObjects(candidates, detailedSnapshot);

            Assert.True(candidates[0].HasDisposedInstances);
            Assert.Equal(LeakConfidence.Confirmed, candidates[0].Confidence);
            Assert.Contains(LeakPattern.GrowthAndDisposed, candidates[0].DetectedPatterns);
        }

        [Fact]
        public void LeakReport_BuildsSummary()
        {
            // #9: Verify report generation
            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 5; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    gen2Size: 1000000 + i * 100000,
                    workingSet: 50000000 + i * 1000000,
                    gcHeapSize: 10000000 + i * 500000,
                    ("MyApp.Leak", 100 + i * 10, (ulong)(1000 + i * 100))
                ));
            }

            var candidates = new List<LeakCandidate>
            {
                new LeakCandidate
                {
                    TypeName = "MyApp.Leak",
                    InitialCount = 100,
                    CurrentCount = 140,
                    Confidence = LeakConfidence.High,
                    Pattern = LeakPattern.ClassicLeak,
                    DetectedPatterns = new List<LeakPattern> { LeakPattern.ClassicLeak },
                    TrendRSquared = 0.99,
                    TrendSlope = 10.0,
                    GrowthRatePerInterval = 10.0,
                    InitialTotalSize = 1000,
                    CurrentTotalSize = 1400,
                    InitialAverageSize = 10,
                    CurrentAverageSize = 10
                }
            };

            var settings = new AutoModeSettings { SnapshotIntervalSeconds = 60 };
            var report = LeakReport.Build(snapshots, candidates, settings, 5);

            Assert.Equal(5, report.TotalSnapshotsTaken);
            Assert.Equal("TestProcess", report.ProcessName);
            Assert.Single(report.LeakCandidates);
            Assert.Equal(5, report.WorkingSetTrend.Count);
            Assert.Equal(5, report.Gen2Trend.Count);

            string summary = report.BuildSummary();
            Assert.Contains("MemoryWatchDog", summary);
            Assert.Contains("MyApp.Leak", summary);
            Assert.Contains("Classic Leak", summary);
        }

        [Fact]
        public void Analyze_AverageSizeTracking()
        {
            // #5: Verify average size tracking is populated
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                // Size grows faster than count — average size increases
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.FatObjects", 100 + i * 10, (ulong)(10000 + i * 5000))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            Assert.True(result[0].InitialAverageSize > 0);
            Assert.True(result[0].CurrentAverageSize > 0);
        }

        [Fact]
        public void Analyze_PatternClassification_ClassicLeak()
        {
            // #6: Classic leak — count and size both grow
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 8; i++)
            {
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.ClassicLeak", 100 + i * 20, (ulong)(1000 + i * 200))
                ));
            }

            var result = detector.Analyze(snapshots, settings);

            Assert.Single(result);
            Assert.Contains(LeakPattern.ClassicLeak, result[0].DetectedPatterns);
        }

        [Fact]
        public void Reset_ClearsHistory()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                snapshots.Add(CreateSnapshot(baseTime.AddMinutes(i), ("MyApp.Leak", 100 + i * 10, (ulong)(1000 + i * 100))));
            }

            detector.Analyze(snapshots, settings);
            detector.Reset();

            // After reset, analyzing stable data should yield no candidates
            // (previously it might have candidate history)
            var stableSnapshots = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                stableSnapshots.Add(CreateSnapshot(baseTime.AddMinutes(10 + i), ("MyApp.Leak", 200, 2000)));
            }

            var result = detector.Analyze(stableSnapshots, settings);
            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_DismissesType_WhenGCReleasesObjects()
        {
            // Issue #3: When GC releases objects, the type should be dismissed quickly
            // even if the overall trend was positive from earlier growth
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0,
                MinTrendRSquared = 0.3
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                // Growth phase
                CreateSnapshot(baseTime, ("MyApp.GCTarget", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.GCTarget", 120, 12000)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.GCTarget", 140, 14000)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.GCTarget", 160, 16000)),
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.GCTarget", 180, 18000)),
                // GC kicks in — count drops in the last 2 snapshots
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.GCTarget", 120, 12000)),
                CreateSnapshot(baseTime.AddMinutes(6), ("MyApp.GCTarget", 80, 8000)),
            };

            var result = detector.Analyze(snapshots, settings);

            // Type should NOT be flagged because recent direction is declining
            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_DismissesType_WhenRecoveredToBaseline()
        {
            // Type grew then returned near its origin count — should disappear from candidates
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0,
                MinAbsoluteGrowth = 10,
                RecoveryTolerancePercent = 0.1
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.TempObjects", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.TempObjects", 200, 20000)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.TempObjects", 300, 30000)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.TempObjects", 400, 40000)),
                // GC collects — back near origin
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.TempObjects", 110, 11000)),
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.TempObjects", 105, 10500)),
            };

            var result = detector.Analyze(snapshots, settings);

            // Type recovered to within 10% of baseline (100) — should NOT be flagged
            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_IgnoresSmallAbsoluteGrowth()
        {
            // Type grows by only a few objects — not meaningful
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 1.0,
                MinAbsoluteGrowth = 10
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.TinyGrowth", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.TinyGrowth", 101, 10100)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.TinyGrowth", 102, 10200)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.TinyGrowth", 103, 10300)),
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.TinyGrowth", 104, 10400)),
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.TinyGrowth", 105, 10500)),
            };

            var result = detector.Analyze(snapshots, settings);

            // Only +5 objects from baseline — below MinAbsoluteGrowth of 10
            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_DismissesType_WhenPeakDropsOver20Percent()
        {
            // Type grew to a peak, then dropped >20% from peak — recovering, not leaking
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0,
                MinAbsoluteGrowth = 10
            };

            var baseTime = DateTime.Now;
            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(baseTime, ("MyApp.PeakDrop", 100, 10000)),
                CreateSnapshot(baseTime.AddMinutes(1), ("MyApp.PeakDrop", 150, 15000)),
                CreateSnapshot(baseTime.AddMinutes(2), ("MyApp.PeakDrop", 200, 20000)),
                CreateSnapshot(baseTime.AddMinutes(3), ("MyApp.PeakDrop", 250, 25000)),
                CreateSnapshot(baseTime.AddMinutes(4), ("MyApp.PeakDrop", 300, 30000)),  // peak
                // Drops >20% from peak
                CreateSnapshot(baseTime.AddMinutes(5), ("MyApp.PeakDrop", 230, 23000)),
                CreateSnapshot(baseTime.AddMinutes(6), ("MyApp.PeakDrop", 235, 23500)),
            };

            var result = detector.Analyze(snapshots, settings);

            // Current (235) is < 80% of peak (300=240) — should be dismissed
            Assert.Empty(result);
        }

        [Fact]
        public void Analyze_DemotesStableCandidates_EvenIfStillDetected()
        {
            // Bug fix: A type that keeps appearing as a candidate but has a flat/stable trend
            // (slope < 0.5, no consecutive growth) should be demoted after DemotionStableIntervals
            // even though AnalyzeType still returns it each cycle.
            var detector = new LeakDetector();
            var settings = new AutoModeSettings
            {
                WarmupSnapshotCount = 3,
                MinConsecutiveGrowthCount = 3,
                MinGrowthRatePercent = 5.0,
                MinTrendRSquared = 0.3,
                MinAbsoluteGrowth = 5,
                DemotionStableIntervals = 3
            };

            var baseTime = DateTime.Now;

            // Build snapshots where the type grew initially, then stays flat at a higher level.
            // The overall regression across 20 snapshots still shows a mild positive trend
            // because of the initial jump, but the last 14 are flat.
            var snapshots = new List<MemoryStats>();
            for (int i = 0; i < 6; i++)
            {
                // Growth phase: 100 → 200
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.StableArray", 100 + i * 20, (ulong)(10000 + i * 20000))
                ));
            }
            for (int i = 6; i < 20; i++)
            {
                // Stable phase at 200
                snapshots.Add(CreateSnapshot(
                    baseTime.AddMinutes(i),
                    ("MyApp.StableArray", 200, (ulong)50000)
                ));
            }

            // Run analysis multiple times — the candidate should appear initially
            // but get demoted after DemotionStableIntervals cycles
            var result1 = detector.Analyze(snapshots, settings);
            var result2 = detector.Analyze(snapshots, settings);
            var result3 = detector.Analyze(snapshots, settings);
            var result4 = detector.Analyze(snapshots, settings);

            // After enough cycles with stable trend, should be removed
            Assert.Empty(result4);
        }
    }
}
