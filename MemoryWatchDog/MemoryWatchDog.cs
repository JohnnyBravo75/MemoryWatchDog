namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime;
    using System.Threading;
    using System.Timers;
    using Microsoft.Diagnostics.Runtime;

    public partial class MemoryWatchDog : IDisposable
    {
        System.Timers.Timer checkTimer = new System.Timers.Timer();

        private bool isWatching = false;

        public long MinMemoryCleanupLimitBytes { get; set; } = 0;

        public bool WriteMemStatsFile { get; set; } = false;

        public string MemStatsFilePath { get; set; }

        public MemStatsFileFormats MemStatsFileFormat { get; set; } = MemStatsFileFormats.txt;

        public TimeSpan CheckInterval { get; private set; }

        public MemoryStatsFilter MemStatsFilter { get; set; } = new MemoryStatsFilter();

        public event EventHandler<MemoryStatsTakenEventArgs> SnapshotTaken;

        public event EventHandler<CaptureProgressEventArgs> CaptureProgress;

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





        public MemoryStats GetMemoryStats(MemoryStatsFilter memoryStatsFilter = null, int? processId = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (memoryStatsFilter == null)
            {
                memoryStatsFilter = new MemoryStatsFilter();
            }

            var memoryStats = new MemoryStats();

            ClrRuntime runtime = null;
            try
            {
                runtime = ClrReader.AttachToClr(processId);

                cancellationToken.ThrowIfCancellationRequested();

                memoryStats.CaptureDate = DateTime.UtcNow;
                memoryStats.ProcessId = processId.Value;
                try
                {
                    memoryStats.ProcessName = Process.GetProcessById(processId.Value).ProcessName;
                }
                catch
                {  // Process may have exited
                }
                memoryStats.NETVersion = runtime.ClrInfo.Version?.ToString();
                memoryStats.CpuUtilizationPercent = runtime.ThreadPool.CpuUtilization;

                memoryStats.ActiveWorkerThreads = runtime.ThreadPool.ActiveWorkerThreads;
                memoryStats.IdleWorkerThreads = runtime.ThreadPool.IdleWorkerThreads;
                memoryStats.WindowsThreadPoolThreadCount = runtime.ThreadPool.WindowsThreadPoolThreadCount;
                memoryStats.MaxThreads = runtime.ThreadPool.MaxThreads;

                // Threads
                this.ReadThreads(runtime, memoryStats, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                // Heap (Objects in Memory)
                this.ReadHeap(runtime, memoryStats, memoryStatsFilter, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                // Filter by max objects count
                FilterByMaxObjects(memoryStats, memoryStatsFilter);

                this.SnapshotTaken?.Invoke(this, new MemoryStatsTakenEventArgs(memoryStats));

                return memoryStats;
            }
            finally
            {
                ClrReader.DetachFromClr(runtime);
            }
        }

        private void FilterByMaxObjects(MemoryStats memoryStats, MemoryStatsFilter memoryStatsFilter)
        {
            foreach (var objKey in memoryStats.Types.Keys.ToList())
            {
                if (memoryStats.Types[objKey].Count < memoryStatsFilter.MinObjectCount)
                {
                    memoryStats.Types.Remove(objKey);
                }
            }
        }

        private void ReadThreads(ClrRuntime runtime, MemoryStats memoryStats, CancellationToken cancellationToken)
        {
            // Resolve thread names from the heap by finding System.Threading.Thread objects
            var threadNames = ClrReader.ResolveThreadNames(runtime);

            foreach (var thread in runtime.Threads.Where(x => x.IsAlive))
            {
                cancellationToken.ThrowIfCancellationRequested();

                threadNames.TryGetValue(thread.ManagedThreadId, out string threadName);

                var threadInfo = new ThreadInfo()
                {
                    Name = threadName,
                    State = thread.State.ToString(),
                    Address = thread.Address,
                    OSThreadId = thread.OSThreadId,
                    ManagedThreadId = thread.ManagedThreadId,
                    IsBackground = thread.State.HasFlag(ClrThreadState.TS_Background),
                    IsThreadPoolThread = thread.State.HasFlag(ClrThreadState.TS_TPWorkerThread)
                                      || thread.State.HasFlag(ClrThreadState.TS_CompletionPortThread),
                    CurrentExceptionType = thread.CurrentException?.Type?.Name
                };

                int idx = 0;
                // Capture managed stack frames
                foreach (var frame in thread.EnumerateStackTrace())
                {
                    string frameName = frame.Method?.Signature ?? frame.FrameName;
                    if (!string.IsNullOrEmpty(frameName))
                    {
                        threadInfo.StackFrames.Add(frameName);
                        idx++;
                    }

                    if (idx > 10)
                    {
                        break;
                    }
                }

                memoryStats.Threads.Add(threadInfo);
            }
        }



        private void ReadHeap(ClrRuntime runtime, MemoryStats memoryStats, MemoryStatsFilter memoryStatsFilter, CancellationToken cancellationToken)
        {
            if (runtime.Heap.CanWalkHeap)
            {
                int objectsProcessed = 0;

                foreach (var obj in runtime.Heap.EnumerateObjects())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    objectsProcessed++;
                    if (objectsProcessed % 1000 == 0)
                    {
                        this.CaptureProgress?.Invoke(this, new CaptureProgressEventArgs(objectsProcessed, memoryStats.Types.Count));
                    }

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
                                AssemblyName = assemblyName,
                                DisplayValue = ClrReader.GetDisplayValue(obj, type)
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

                this.CaptureProgress?.Invoke(this, new CaptureProgressEventArgs(objectsProcessed, memoryStats.Types.Count));
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
