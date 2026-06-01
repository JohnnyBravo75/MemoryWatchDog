namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using Microsoft.Diagnostics.Runtime;

    public class MemoryGrabber
    {
        public long MinMemoryCleanupLimitBytes { get; set; } = 0;

        public bool WriteMemStatsFile { get; set; } = false;

        public string MemStatsFilePath { get; set; }

        public MemStatsFileFormats MemStatsFileFormat { get; set; } = MemStatsFileFormats.txt;

        public MemoryStatsFilter MemStatsFilter { get; set; } = new MemoryStatsFilter();

        public event EventHandler<MemoryStatsTakenEventArgs> SnapshotTaken;

        public event EventHandler<CaptureProgressEventArgs> CaptureProgress;

        public void ForceGC()
        {
            ClrUtil.ForceGC();
        }

        public void ForceRemoteGC(int processId)
        {
            ClrUtil.ForceRemoteGC(processId);
        }

        public string GetNETVersion(int processId)
        {
            return ClrReader.GetNETVersion(processId);
        }

        public void TryGrabAndWrite()
        {
            var memUsage = GC.GetTotalMemory(forceFullCollection: true);
            if (memUsage > this.MinMemoryCleanupLimitBytes)
            {
                this.ForceGC();

                if (this.WriteMemStatsFile)
                {
                    var memStats = this.GetMemoryStats(this.MemStatsFilter);
                    memStats?.WriteToFile(this.MemStatsFilePath, this.MemStatsFileFormat);
                }
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

            var memoryStats = new MemoryStats();

            ClrRuntime runtime = null;
            try
            {
                runtime = ClrReader.AttachToClr(processId);

                cancellationToken.ThrowIfCancellationRequested();

                memoryStats.CaptureDate = DateTime.Now;
                memoryStats.ProcessId = processId.Value;

                // Overview stats
                ReadOverviewStats(runtime, memoryStats);

                // Threads
                if (memoryStatsFilter.CaputureThreads)
                {
                    memoryStats.ActiveWorkerThreads = runtime.ThreadPool.ActiveWorkerThreads;
                    memoryStats.IdleWorkerThreads = runtime.ThreadPool.IdleWorkerThreads;
                    memoryStats.WindowsThreadPoolThreadCount = runtime.ThreadPool.WindowsThreadPoolThreadCount;
                    memoryStats.MaxThreads = runtime.ThreadPool.MaxThreads;

                    this.ReadThreads(runtime, memoryStats, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Heap (Objects in Memory)
                if (memoryStatsFilter.CaputureObjects)
                {
                    this.ReadHeap(runtime, memoryStats, memoryStatsFilter, cancellationToken);
                }

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

        private static void ReadOverviewStats(ClrRuntime runtime, MemoryStats memoryStats)
        {
            try
            {
                var process = Process.GetProcessById(memoryStats.ProcessId);
                process.Refresh();
                memoryStats.ProcessName = process.ProcessName;
                memoryStats.WorkingSet = process.WorkingSet64;
                memoryStats.PrivateBytes = process.PrivateMemorySize64;
            }
            catch
            {  // Process may have exited
            }

            memoryStats.NETVersion = runtime.ClrInfo.Version?.ToString();
            memoryStats.CpuUtilizationPercent = runtime.ThreadPool.CpuUtilization;

            // Collect GC heap segment sizes
            try
            {
                foreach (var segment in runtime.Heap.Segments)
                {
                    long size = (long)segment.Length;
                    memoryStats.GCHeapSize += size;

                    switch (segment.Kind)
                    {
                        case GCSegmentKind.Generation0:
                            memoryStats.Gen0Size += size;
                            break;
                        case GCSegmentKind.Generation1:
                            memoryStats.Gen1Size += size;
                            break;
                        case GCSegmentKind.Generation2:
                            memoryStats.Gen2Size += size;
                            break;
                        case GCSegmentKind.Large:
                            memoryStats.LOHSize += size;
                            break;
                        case GCSegmentKind.Pinned:
                            memoryStats.POHSize += size;
                            break;
                    }
                }
            }
            catch
            {
                // Segment enumeration can fail under contention
            }
        }

        private static void FilterByMaxObjects(MemoryStats memoryStats, MemoryStatsFilter memoryStatsFilter)
        {
            if (memoryStatsFilter.MinObjectCount <= 1)
            {
                return;
            }

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

                var staticRootAddresses = ClrReader.BuildStaticRootAddresses(runtime);

                // Lookup to reuse already-created ObjectInfo instances for references
                var objectsByAddress = !memoryStatsFilter.AggregateObjects
                    ? new Dictionary<ulong, ObjectInfo>()
                    : null;

                foreach (var obj in runtime.Heap.EnumerateObjects())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    objectsProcessed++;
                    if (objectsProcessed % 1000 == 0)
                    {
                        this.CaptureProgress?.Invoke(this, new CaptureProgressEventArgs(objectsProcessed, memoryStats.Types.Count, $"Objects: {objectsProcessed} | Types: {memoryStats.Types.Count}"));
                    }

                    try
                    {
                        var type = obj.Type;

                        if (type != null)
                        {
                            string typeName = type?.Name;
                            string typeNamespace = CommonUtil.GetNamespaceFromTypeName(typeName);
                            string assemblyName = type.Module?.AssemblyName ?? "Unknown Assembly";

                            memoryStats.TotalSize += (long)obj.Size;

                            // Filter by exact type names (for targeted leak snapshots)
                            if (memoryStatsFilter.IncludeTypeNames?.Count > 0 &&
                                !memoryStatsFilter.IncludeTypeNames.Contains(typeName))
                            {
                                continue;
                            }

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

                            var isSystemObj = ClrReader.IsSystemType(type);

                            ObjectInfo objInfo;
                            if (!memoryStatsFilter.AggregateObjects && objectsByAddress.TryGetValue(obj.Address, out objInfo))
                            {
                                // A stub was already created when a parent object referenced this address.
                                // Upgrade it in-place so all existing parent References lists see the full data.
                                UpgradeObjectInfo(objInfo, obj, type, memoryStatsFilter, staticRootAddresses, isSystemObj);
                            }
                            else
                            {
                                objInfo = CreateObjectInfo(obj, type, memoryStatsFilter, staticRootAddresses, isSystemObj);
                            }

                            if (!memoryStatsFilter.AggregateObjects)
                            {
                                // Register / overwrite so later forward-references find the canonical instance
                                objectsByAddress[obj.Address] = objInfo;

                                // Build field name lookup: address → field name for this object's fields
                                var fieldNames = !isSystemObj ? ClrReader.GetReferenceFieldNames(obj, type) : null;

                                // Enumerate references from this object
                                foreach (var refObj in obj.EnumerateReferences())
                                {
                                    ObjectInfo refObjInfo;

                                    // Reuse an already-scanned ObjectInfo if available
                                    if (!objectsByAddress.TryGetValue(refObj.Address, out refObjInfo))
                                    {
                                        var isSystemRefObj = ClrReader.IsSystemType(refObj.Type);
                                        refObjInfo = new ObjectInfo
                                        {
                                            Reference = refObj,
                                            TypeName = refObj.Type?.Name ?? "Unknown",
                                            Size = refObj.Size,
                                            ElementType = refObj.Type?.ElementType.ToString(),
                                            Address = refObj.Address,
                                            AssemblyName = refObj.Type?.Module?.AssemblyName ?? "Unknown Assembly",
                                            DisplayValue = memoryStatsFilter.CaptureDisplayValues && !isSystemRefObj ? ClrReader.GetDisplayValue(refObj, refObj.Type) : "",
                                        };
                                        // Register stub immediately so other parents can share the same instance
                                        // and the heap loop can upgrade it in-place later.
                                        objectsByAddress[refObj.Address] = refObjInfo;
                                    }

                                    if (fieldNames != null && fieldNames.TryGetValue(refObj.Address, out var fieldName))
                                    {
                                        refObjInfo.FieldName = fieldName;
                                    }

                                    objInfo.References.Add(refObjInfo);
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

                this.CaptureProgress?.Invoke(this, new CaptureProgressEventArgs(objectsProcessed, memoryStats.Types.Count, $"Objects: {objectsProcessed} | Types: {memoryStats.Types.Count}"));
            }
        }

        /// <summary>
        /// Fills in full details on an ObjectInfo that was previously created as a forward-reference stub.
        /// Called when the heap enumerator finally visits the object the stub points to.
        /// </summary>
        private static void UpgradeObjectInfo(ObjectInfo objInfo, ClrObject obj, ClrType type, MemoryStatsFilter memoryStatsFilter, HashSet<ulong> staticRootAddresses, bool isSystemObj)
        {
            objInfo.Reference = obj;
            objInfo.TypeName = type?.Name;
            objInfo.Size = obj.Size;
            objInfo.ElementType = type?.ElementType.ToString();
            objInfo.AssemblyName = type?.Module?.AssemblyName ?? "Unknown Assembly";

            if (!isSystemObj)
            {
                objInfo.DisplayValue = memoryStatsFilter.CaptureDisplayValues ? ClrReader.GetDisplayValue(obj, type) : "";
                objInfo.IsDisposed = ClrReader.IsObjectDisposed(obj, type);
                objInfo.IsStatic = staticRootAddresses.Contains(obj.Address);
                objInfo.IsEventHandler = ClrReader.IsEventHandler(type);
            }
        }

        private static ObjectInfo CreateObjectInfo(ClrObject obj, ClrType type, MemoryStatsFilter memoryStatsFilter, HashSet<ulong> staticRootAddresses, bool isSystemObj)
        {
            var objInfo = new ObjectInfo()
            {
                Reference = obj,
                TypeName = type?.Name,
                Size = obj.Size,
                ElementType = type?.ElementType.ToString(),
                Address = obj.Address,
                AssemblyName = type?.Module?.AssemblyName ?? "Unknown Assembly",
            };

            if (!memoryStatsFilter.AggregateObjects)
            {
                if (!isSystemObj)
                {
                    objInfo.DisplayValue = memoryStatsFilter.CaptureDisplayValues ? ClrReader.GetDisplayValue(obj, type) : "";
                    objInfo.IsDisposed = ClrReader.IsObjectDisposed(obj, type);
                    objInfo.IsStatic = staticRootAddresses.Contains(obj.Address);
                    objInfo.IsEventHandler = ClrReader.IsEventHandler(type);
                }
            }

            return objInfo;
        }
    }
}
