namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Diagnostics.NETCore.Client;
    using Microsoft.Diagnostics.Runtime;

    public class ClrUtil
    {
        public static ClrRuntime AttachToClr(int? processId = null)
        {
            if (processId == null)
            {
                processId = Process.GetCurrentProcess().Id;
            }

            var dataTarget = DataTarget.AttachToProcess(processId.Value, suspend: false);

            if (dataTarget.ClrVersions.Count() == 0)
            {
                throw new InvalidOperationException($"Target process (pid={processId}) is not a .NET process.");
            }

            var clrInfo = dataTarget.ClrVersions[0];

            var runtime = clrInfo.CreateRuntime();

            return runtime;
        }

        public static void DetachFromClr(ClrRuntime runtime)
        {
            runtime?.Dispose();
            runtime?.DataTarget?.Dispose();
        }

        public static Dictionary<int, string> ResolveThreadNames(ClrRuntime runtime)
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

        public static string GetDisplayValue(ClrObject obj, ClrType type)
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
                if (typeName != null && IsCollectionType(typeName))
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

                    string displayValue = TryReadFieldValue(obj, field);
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

        private static string TryReadFieldValue(ClrObject obj, ClrInstanceField field)
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

        private static bool IsCollectionType(string typeName)
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

        public static string GetNETVersion(int processId)
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

        private static readonly HashSet<string> DisposedFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "disposed", "_disposed", "_isDisposed", "disposedValue", "_disposedValue", "isDisposed"
        };

        public static bool IsObjectDisposed(ClrObject obj, ClrType type)
        {
            try
            {
                if (type == null || type.Fields == null)
                {
                    return false;
                }

                foreach (var field in type.Fields)
                {
                    if (field?.Name != null
                        && DisposedFieldNames.Contains(field.Name)
                        && field.ElementType == ClrElementType.Boolean)
                    {
                        bool value = field.Read<bool>(obj.Address, interior: false);
                        if (value)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Field read can fail for corrupted or partially collected objects
            }

            return false;
        }

        public static void ForceGC()
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

        public static void ForceRemoteGC(int processId)
        {
            var client = new DiagnosticsClient(processId);
            var providers = new List<EventPipeProvider>
            {
                new EventPipeProvider(
                    "Microsoft-Windows-DotNETRuntime",
                    System.Diagnostics.Tracing.EventLevel.Informational,
                    (long)0x800000) // GCHeapCollect keyword - induces a GC on the target process
            };

            EventPipeSession session = null;
            try
            {
                session = client.StartEventPipeSession(providers, requestRundown: false);

                // Drain the event stream on a background thread to prevent Stop() from deadlocking.
                // Without this, the pipe buffer fills up and Stop() blocks forever waiting for the
                // runtime to acknowledge the stop command.
                var drainTask = Task.Run(() =>
                {
                    try
                    {
                        var buffer = new byte[4096];
                        while (session.EventStream.Read(buffer, 0, buffer.Length) > 0)
                        {
                        }
                    }
                    catch
                    {
                        // Stream will throw when session is stopped, which is expected
                    }
                });

                // Give the runtime time to execute the induced GC
                Thread.Sleep(1000);

                session.Stop();
                drainTask.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                session?.Dispose();
            }
        }

        public static LiveMemorySnapshot CaptureLiveSnapshot(int processId)
        {
            var snapshot = new LiveMemorySnapshot { Timestamp = DateTime.Now };

            // OS-level metrics (throws if process exited)
            var process = Process.GetProcessById(processId);
            process.Refresh();
            snapshot.WorkingSet = process.WorkingSet64;
            snapshot.PrivateBytes = process.PrivateMemorySize64;

            // Managed heap metrics via ClrMD (optional — may fail for non-.NET processes)
            ClrRuntime runtime = null;
            try
            {
                runtime = ClrUtil.AttachToClr(processId);
                foreach (var segment in runtime.Heap.Segments)
                {
                    long size = (long)segment.Length;
                    snapshot.GCHeapSize += size;

                    switch (segment.Kind)
                    {
                        case GCSegmentKind.Generation0:
                            snapshot.Gen0Size += size;
                            break;
                        case GCSegmentKind.Generation1:
                            snapshot.Gen1Size += size;
                            break;
                        case GCSegmentKind.Generation2:
                            snapshot.Gen2Size += size;
                            break;
                        case GCSegmentKind.Large:
                            snapshot.LOHSize += size;
                            break;
                        case GCSegmentKind.Pinned:
                            snapshot.POHSize += size;
                            break;
                        default:
                            break;
                    }
                }
            }
            catch
            {
                // ClrMD attach may fail for non-.NET processes or under contention; keep OS metrics
            }
            finally
            {
                ClrUtil.DetachFromClr(runtime);
            }

            return snapshot;
        }

    }
}
