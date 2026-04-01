namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using Microsoft.Diagnostics.Runtime;

    public class ClrReader
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
    }
}
