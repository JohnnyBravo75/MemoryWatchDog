namespace MemoryWatchDog.Test
{
    public class LeakDetectorTests
    {
        private static MemoryStats CreateSnapshot(DateTime captureDate, params (string typeName, long count, ulong size)[] types)
        {
            var stats = new MemoryStats
            {
                CaptureDate = captureDate,
                ProcessId = 1234,
                ProcessName = "TestProcess"
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

        [Fact]
        public void Analyze_ReturnsEmpty_WhenFewerThanWarmupSnapshots()
        {
            var detector = new LeakDetector();
            var settings = new AutoModeSettings { WarmupSnapshotCount = 5, MinConsecutiveGrowthCount = 3 };

            var snapshots = new List<MemoryStats>
            {
                CreateSnapshot(DateTime.UtcNow, ("MyApp.Foo", 10, 1000)),
                CreateSnapshot(DateTime.UtcNow.AddMinutes(1), ("MyApp.Foo", 20, 2000)),
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

            var baseTime = DateTime.UtcNow;
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

            var baseTime = DateTime.UtcNow;
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
                MinGrowthRatePercent = 5.0
            };

            var baseTime = DateTime.UtcNow;
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

            var baseTime = DateTime.UtcNow;
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
            var baseTime = DateTime.UtcNow;
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
            var baseTime = DateTime.UtcNow;
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

            var baseTime = DateTime.UtcNow;
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

            var baseTime = DateTime.UtcNow;
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
            Assert.Equal(LeakConfidence.High, result[0].Confidence);
        }
    }
}
