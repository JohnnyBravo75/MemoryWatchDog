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

            if (processId == null)
            {
                processId = Process.GetCurrentProcess().Id;
            }

            cancellationToken.ThrowIfCancellationRequested();

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

                cancellationToken.ThrowIfCancellationRequested();

                using (var runtime = clrInfo.CreateRuntime())
                {
                    memoryStats.CpuUtilizationPercent = runtime.ThreadPool.CpuUtilization;

                    // Threads
                    memoryStats.ActiveWorkerThreads = runtime.ThreadPool.ActiveWorkerThreads;
                    memoryStats.IdleWorkerThreads = runtime.ThreadPool.IdleWorkerThreads;
                    memoryStats.WindowsThreadPoolThreadCount = runtime.ThreadPool.WindowsThreadPoolThreadCount;
                    memoryStats.MaxThreads = runtime.ThreadPool.MaxThreads;

                    this.ReadThreads(runtime, memoryStats, cancellationToken);

                    // Heap (Objects in Memory)
                    this.ReadHeap(runtime, memoryStats, memoryStatsFilter, cancellationToken);
                }

            }

            cancellationToken.ThrowIfCancellationRequested();

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

        private void ReadThreads(ClrRuntime runtime, MemoryStats memoryStats, CancellationToken cancellationToken)
        {
            // Resolve thread names from the heap by finding System.Threading.Thread objects
            var threadNames = this.ResolveThreadNames(runtime);

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

        private Dictionary<int, string> ResolveThreadNames(ClrRuntime runtime)
        {
            var names = new Dictionary<int, string>();

            try
            {
                if (!runtime.Heap.CanWalkHeap)
                {
                    return names;
                }

                foreach (var obj in runtime.Heap.EnumerateObjects())
                {
                    if (obj.Type?.Name != "System.Threading.Thread")
                    {
                        continue;
                    }

                    try
                    {
                        var idField = obj.Type.GetFieldByName("_managedThreadId")
                                   ?? obj.Type.GetFieldByName("m_ManagedThreadId");
                        var nameField = obj.Type.GetFieldByName("_name")
                                     ?? obj.Type.GetFieldByName("m_Name");

                        if (idField == null || nameField == null)
                        {
                            continue;
                        }

                        int managedId = idField.Read<int>(obj.Address, interior: false);
                        string name = nameField.ReadString(obj.Address, interior: false);

                        if (!string.IsNullOrEmpty(name) && !names.ContainsKey(managedId))
                        {
                            names[managedId] = name;
                        }
                    }
                    catch
                    {
                        // Skip objects that can't be read
                    }
                }
            }
            catch
            {
                // If heap walk fails, return what we have
            }

            return names;
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
                                DisplayValue = this.GetDisplayValue(obj, type)
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

        private static readonly HashSet<string> IdentityFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name", "_name", "Id", "_id", "Key", "_key", "Text", "_text", "Title", "_title",
            "DisplayName", "_displayName", "Label", "_label", "Description", "_description"
        };

        private static readonly HashSet<ClrElementType> ReadableValueTypes = new HashSet<ClrElementType>
        {
            ClrElementType.Boolean,
            ClrElementType.Int8, ClrElementType.UInt8,
            ClrElementType.Int16, ClrElementType.UInt16,
            ClrElementType.Int32, ClrElementType.UInt32,
            ClrElementType.Int64, ClrElementType.UInt64,
            ClrElementType.Float, ClrElementType.Double
        };

        private string GetDisplayValue(ClrObject obj, ClrType type)
        {
            try
            {
                if (type.IsString)
                {
                    string value = obj.AsString(maxLength: 120);
                    if (value != null)
                    {
                        return $"\"{value}\"";
                    }
                    return "";
                }

                // Skip types where identity fields are meaningless or GetFieldByName may hang
                string typeName = type.Name;
                if (typeName != null && this.IsCollectionType(typeName))
                {
                    return "";
                }

                // Iterate fields once instead of calling GetFieldByName per name (avoids hangs on complex generic types)
                var fields = type.Fields;
                if (fields == null)
                {
                    return "";
                }

                foreach (var field in fields)
                {
                    if (field?.Name == null || !IdentityFieldNames.Contains(field.Name))
                    {
                        continue;
                    }

                    string displayValue = this.TryReadFieldValue(obj, field);
                    if (!string.IsNullOrEmpty(displayValue))
                    {
                        return $"{field.Name} = {displayValue}";
                    }
                }
            }
            catch
            {
                // Reading fields can fail for corrupted or partially collected objects
            }

            return "";
        }

        private string TryReadFieldValue(ClrObject obj, ClrInstanceField field)
        {
            try
            {
                if (field.ElementType == ClrElementType.String)
                {
                    string value = obj.ReadStringField(field.Name);
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (value.Length > 80)
                        {
                            value = value.Substring(0, 80) + "...";
                        }
                        return $"\"{value}\"";
                    }
                }
                else if (ReadableValueTypes.Contains(field.ElementType))
                {
                    switch (field.ElementType)
                    {
                        case ClrElementType.Boolean:
                            return obj.ReadField<bool>(field.Name).ToString();
                        case ClrElementType.Int32:
                            return obj.ReadField<int>(field.Name).ToString();
                        case ClrElementType.Int64:
                            return obj.ReadField<long>(field.Name).ToString();
                        case ClrElementType.UInt32:
                            return obj.ReadField<uint>(field.Name).ToString();
                        case ClrElementType.UInt64:
                            return obj.ReadField<ulong>(field.Name).ToString();
                        case ClrElementType.Float:
                            return obj.ReadField<float>(field.Name).ToString();
                        case ClrElementType.Double:
                            return obj.ReadField<double>(field.Name).ToString();
                        case ClrElementType.Int16:
                            return obj.ReadField<short>(field.Name).ToString();
                        case ClrElementType.UInt16:
                            return obj.ReadField<ushort>(field.Name).ToString();
                        case ClrElementType.Int8:
                            return obj.ReadField<sbyte>(field.Name).ToString();
                        case ClrElementType.UInt8:
                            return obj.ReadField<byte>(field.Name).ToString();
                    }
                }
            }
            catch
            {
                // Field read failed
            }

            return null;
        }

        private bool IsCollectionType(string typeName)
        {
            return typeName.StartsWith("System.Collections.", StringComparison.Ordinal)
                || typeName.StartsWith("System.Dictionary", StringComparison.Ordinal)
                || typeName.Contains("Dictionary<")
                || typeName.Contains("List<")
                || typeName.Contains("HashSet<")
                || typeName.Contains("Queue<")
                || typeName.Contains("Stack<")
                || typeName.Contains("ConcurrentDictionary<")
                || typeName.Contains("ConcurrentQueue<")
                || typeName.Contains("ConcurrentBag<")
                || typeName.StartsWith("System.Linq.", StringComparison.Ordinal);
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
