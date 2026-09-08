namespace MemoryWatchDog.Application
{
    using System;
    using System.Collections.Generic;
    using MemoryWatchDog.Core;

    public class SnapshotService
    {
        public SnapshotRecord AddSnapshot(ICollection<SnapshotRecord> snapshots, MemoryStats stats, bool isAutoSnapshot = false)
        {
            if (snapshots == null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }

            var item = new SnapshotRecord(stats, isAutoSnapshot);
            snapshots.Add(item);
            return item;
        }

        public void ClearSnapshots(ICollection<SnapshotRecord> snapshots)
        {
            if (snapshots == null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }

            foreach (var snapshot in snapshots)
            {
                this.ReleaseSnapshot(snapshot);
            }

            snapshots.Clear();
        }

        public int RemoveSnapshot(IList<SnapshotRecord> snapshots, SnapshotRecord snapshot)
        {
            if (snapshots == null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            int index = snapshots.IndexOf(snapshot);
            if (index < 0)
            {
                return snapshots.Count > 0 ? 0 : -1;
            }

            this.ReleaseSnapshot(snapshot);
            snapshots.RemoveAt(index);

            return snapshots.Count > 0
                ? Math.Min(index, snapshots.Count - 1)
                : -1;
        }

        public (MemoryStats Older, MemoryStats Newer) GetChronologicalPair(SnapshotRecord first, SnapshotRecord second)
        {
            if (first == null)
            {
                throw new ArgumentNullException(nameof(first));
            }

            if (second == null)
            {
                throw new ArgumentNullException(nameof(second));
            }

            if (first.Stats == null || second.Stats == null)
            {
                throw new InvalidOperationException("Cannot compare snapshots without memory stats.");
            }

            return first.Stats.CaptureDate <= second.Stats.CaptureDate
                ? (first.Stats, second.Stats)
                : (second.Stats, first.Stats);
        }

        private void ReleaseSnapshot(SnapshotRecord snapshot)
        {
            if (snapshot?.Stats == null)
            {
                return;
            }

            snapshot.Stats.Clear();
            snapshot.Stats = null;
        }
    }
}
