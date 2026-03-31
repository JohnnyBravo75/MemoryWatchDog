namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime;
    using System.Timers;
    using Microsoft.Diagnostics.Runtime;

    public partial class MemoryWatchDog : IDisposable
    {
        Timer checkTimer = new Timer();

        private bool isWatching = false;

        public long MinMemoryCleanupLimitBytes { get; set; } = 0;

        public bool WriteMemStatsFile { get; set; } = false;

        public string MemStatsFilePath { get; set; }

        public MemStatsFileFormats MemStatsFileFormat { get; set; } = MemStatsFileFormats.txt;

        public TimeSpan CheckInterval { get; private set; }

        public MemoryStatsFilter MemStatsFilter { get; set; } = new MemoryStatsFilter();

        public event EventHandler<MemoryStatsTakenEventArgs> SnapshotTaken;

        public MemoryWatchDog()
        {
            this.checkTimer.Elapsed += this.CheckTimer_Tick;
        }

        public bool IsWatching
        {
            get { return this.checkTimer != null && this.isWatching; }
        }

        public void StartWatching(TimeSpan checkInterval)
        {
            this.CheckInterval = checkInterval;

            if (this.checkTimer != null && !this.checkTimer.Enabled)
            {
                this.checkTimer.Interval = checkInterval.TotalMilliseconds;
                this.checkTimer.Start();
                this.isWatching = true;
            }
        }

        public void StopWatching()
        {
            if (this.checkTimer != null && this.checkTimer.Enabled)
            {
                this.checkTimer.Stop();
                this.isWatching = false;
            }
        }

        public void Dispose()
        {
            if (this.checkTimer != null)
            {
                this.StopWatching();

                this.isWatching = false;
                this.checkTimer.Elapsed -= this.CheckTimer_Tick;
                this.checkTimer.Dispose();
                this.checkTimer = null;
            }
        }

        private void CheckTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                this.checkTimer?.Stop();

                var memUsage = GC.GetTotalMemory(forceFullCollection: true);
                if (memUsage > this.MinMemoryCleanupLimitBytes)
                {
                    this.CleanupMemory();

                    if (this.WriteMemStatsFile)
                    {
                        var memStats = this.GetMemoryStats(this.MemStatsFilter);
                        memStats?.WriteToFile(this.MemStatsFilePath, this.MemStatsFileFormat);
                    }
                }

                this.checkTimer?.Start();
            }
            catch
            {
                // ignore
            }
        }

        public void CleanupMemory()
        {
            try
            {
                GC.Collect();

                // This is for compression the LOH (Large Object Heap) - this is not done by defualt and could fragment your memory and and memory could grow
                // https://web.archive.org/web/20201027035717/https://www.wintellect.com/hey-who-stole-all-my-memory/
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }
            catch
            {
                // ignore
            }
        }

        public string GetNETVersion(int processId)
        {
            try
            {
                using (var dataTarget = DataTarget.AttachToProcess(processId, suspend: false))
                {
                    var clrInfo = dataTarget.ClrVersions.FirstOrDefault();
                    if (clrInfo != null)
                    {
                        return clrInfo.Version?.ToString();
                    }
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        public MemoryStats GetMemoryStats(MemoryStatsFilter memoryStatsFilter = null, int? processId = null)
        {
            if (memoryStatsFilter == null)
            {
                memoryStatsFilter = new MemoryStatsFilter();
            }

            if (processId == null)
            {
                processId = Process.GetCurrentProcess().Id;
            }

            var memoryStats = new MemoryStats
            {
                CaptureDate = DateTime.UtcNow,
                ProcessId = processId.Value
            };

            try
            {

                memoryStats.ProcessName = Process.GetProcessById(processId.Value).ProcessName;
            }
            catch
            {
                // Process may have exited
            }

            using (var dataTarget = DataTarget.AttachToProcess(processId.Value, suspend: false))
            {
                if (dataTarget.ClrVersions.Count() == 0)
                {
                    throw new InvalidOperationException($"Target process (pid={processId}) is not a .NET process.");
                }

                var clrInfo = dataTarget.ClrVersions[0];
                memoryStats.NETVersion = clrInfo.Version?.ToString();

                using (var runtime = clrInfo.CreateRuntime())
                {
                    memoryStats.CpuUtilizationPercent = runtime.ThreadPool.CpuUtilization;

                    // Threads
                    memoryStats.ActiveWorkerThreads = runtime.ThreadPool.ActiveWorkerThreads;
                    memoryStats.IdleWorkerThreads = runtime.ThreadPool.IdleWorkerThreads;
                    memoryStats.WindowsThreadPoolThreadCount = runtime.ThreadPool.WindowsThreadPoolThreadCount;
                    memoryStats.MaxThreads = runtime.ThreadPool.MaxThreads;

                    this.ReadThreads(runtime, memoryStats);

                    // Heap (Objects in Memory)
                    this.ReadHeap(runtime, memoryStats, memoryStatsFilter);
                }

            }

            // Filter by max objects count
            foreach (var objKey in memoryStats.Types.Keys.ToList())
            {
                if (memoryStats.Types[objKey].Count < memoryStatsFilter.MinObjectCount)
                {
                    memoryStats.Types.Remove(objKey);
                }
            }

            this.SnapshotTaken?.Invoke(this, new MemoryStatsTakenEventArgs(memoryStats));

            return memoryStats;
        }

        private void ReadThreads(ClrRuntime runtime, MemoryStats memoryStats)
        {
            foreach (var thread in runtime.Threads.Where(x => x.IsAlive))
            {
                var threadInfo = new ThreadInfo()
                {
                    State = thread.State.ToString(),
                    Address = thread.Address,
                    OSThreadId = thread.OSThreadId,
                    ManagedThreadId = thread.ManagedThreadId
                };
                memoryStats.Threads.Add(threadInfo);
            }
        }

        private void ReadHeap(ClrRuntime runtime, MemoryStats memoryStats, MemoryStatsFilter memoryStatsFilter)
        {
            if (runtime.Heap.CanWalkHeap)
            {
                foreach (var obj in runtime.Heap.EnumerateObjects())
                {
                    try
                    {
                        var type = obj.Type;

                        if (type != null)
                        {
                            string typeName = type?.Name;
                            string typeNamespace = this.GetNamespaceFromTypeName(typeName);
                            string assemblyName = type.Module?.AssemblyName ?? "Unknown Assembly";

                            memoryStats.TotalSize += (long)obj.Size;

                            // Filtern nach Namespace   
                            if (memoryStatsFilter.ExcludeNameSpaces?.Count > 0 &&
                                memoryStatsFilter.IsInNamespace(typeNamespace, memoryStatsFilter.ExcludeNameSpaces))
                            {
                                continue;
                            }

                            if (memoryStatsFilter.IncludeNameSpaces?.Count > 0 &&
                                !memoryStatsFilter.IsInNamespace(typeNamespace, memoryStatsFilter.IncludeNameSpaces))
                            {
                                continue;
                            }

                            var objInfo = new ObjectInfo()
                            {
                                Reference = obj,
                                TypeName = typeName,
                                Size = obj.Size,
                                ElementType = type.ElementType.ToString(),
                                AssemblyName = assemblyName
                            };

                            if (!memoryStatsFilter.AggregateObjects)
                            {
                                // Enumerate references from this object
                                foreach (var reference in obj.EnumerateReferences())
                                {
                                    objInfo.References.Add(new ReferenceInfo
                                    {
                                        TypeName = reference.Type?.Name ?? "Unknown",
                                        Address = reference.Address,
                                        Size = reference.Size
                                    });
                                }
                            }

                            memoryStats.AddObject(objInfo, aggregate: memoryStatsFilter.AggregateObjects);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Object: {obj} Error: {ex.Message}");
                    }
                }

            }
        }

        private string GetNamespaceFromTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return "Unknown Namespace";
            }

            int lastDotIndex = typeName.LastIndexOf('.');
            if (lastDotIndex > 0)
            {
                return typeName.Substring(0, lastDotIndex);  // Extract namespace (should be done by a regex, this approach is not complete)
            }
            else
            {
                return "Global Namespace";  // No dot found, global namespace
            }
        }
    }
}
