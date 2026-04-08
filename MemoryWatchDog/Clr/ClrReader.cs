namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Diagnostics.NETCore.Client;
    using Microsoft.Diagnostics.Runtime;

    public class ClrReader
    {
        static List<string> systemNamespaces = GetSystemNamespaces();

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

        public static List<string> GetSystemNamespaces()
        {
            return new List<string>() { "<>", "System", "Microsoft", "Windows", "mscorlib", "MS.", "Global", "Global Namespace", "<CppImplementationDetails>", "<CrtImplementationDetails>", "Internal." };
        }

        public static bool IsSystemType(ClrType type)
        {
            if (type == null)
            {
                return false;
            }

            foreach (var systemNamespace in systemNamespaces)
            {
                if (type.Name.StartsWith(systemNamespace))
                {
                    return true;
                }
            }
            return false;
        }

        public static string GetDisplayValue(ClrObject obj, ClrType type)
        {
            if (type == null)
            {
                return "";
            }

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

                // Skip types where GetFieldByName may hang
                string typeName = type.Name;
                if (typeName != null && IsCollectionType(typeName))
                {
                    return "";
                }

                string displayValue = "";
                var fields = GetFields(obj, type, maxFields: 20);
                foreach (var field in fields)
                {
                    displayValue += $"{field.Key} = {field.Value ?? "null"};  ";
                }

                return displayValue.Trim();
            }
            catch
            {
                // Reading fields can fail for corrupted or partially collected objects
            }

            return "";
        }

        public static Dictionary<ulong, string> GetReferenceFieldNames(ClrObject obj, ClrType type)
        {
            var result = new Dictionary<ulong, string>();

            if (type == null)
            {
                return result;
            }

            string typeName = type.Name;
            if (typeName != null && IsCollectionType(typeName))
            {
                return result;
            }

            var fields = type.Fields;
            if (fields == null)
            {
                return result;
            }

            foreach (var field in fields)
            {
                if (field?.Name == null || !field.IsObjectReference)
                {
                    continue;
                }

                try
                {
                    var refObj = field.ReadObject(obj.Address, interior: false);
                    if (refObj.Address != 0 && !result.ContainsKey(refObj.Address))
                    {
                        result[refObj.Address] = GetReadableFieldName(field.Name);
                    }
                }
                catch
                {
                    // Field read can fail for corrupted or partially collected objects
                }
            }

            return result;
        }

        public static Dictionary<string, object> GetFields(ClrObject obj, ClrType type, int maxFields = 20, bool onlyWithValues = true)
        {
            var result = new Dictionary<string, object>();

            if (type == null)
            {
                return result;
            }

            // Skip collection types there may be problems (hang) or not informative infos
            string typeName = type.Name;
            if (typeName != null && IsCollectionType(typeName))
            {
                return result;
            }

            var fields = type.Fields;
            if (fields == null)
            {
                return result;
            }

            // Iterate fields once instead of calling GetFieldByName per name (avoids hangs on complex generic types)
            // Order fields so that own/custom type fields come first and system type fields come last

            //var orderedFields = fields
            //    .Where(f => f?.Name != null)
            //    .OrderBy(f => f.ContainingType != null && IsSystemType(f.ContainingType) ? 1 : 0)
            //    .ToList();

            int fieldCount = 0;
            foreach (var field in fields)
            {
                string fieldValue = TryReadFieldValue(obj, field);

                if ((onlyWithValues && !string.IsNullOrEmpty(fieldValue))
                   || !onlyWithValues)
                {
                    var readableFieldName = GetReadableFieldName(field.Name);
                    result.Add(readableFieldName, fieldValue);
                }

                fieldCount++;
                if (fieldCount >= maxFields)
                {
                    return result;
                }
            }

            return result;
        }

        private static string GetReadableFieldName(string fieldName)
        {
            string pattern = @"<([^>]+)>k__BackingField";

            Match match = Regex.Match(fieldName, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1]?.Value ?? fieldName;
            }

            return fieldName;
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

        public static bool IsEventHandler(ClrType type)
        {
            if (type == null)
            {
                return false;
            }

            try
            {
                var baseType = type?.BaseType;
                while (baseType != null)
                {
                    if (baseType.Name == "System.MulticastDelegate" || baseType.Name == "System.Delegate")
                    {
                        return true;
                    }
                    baseType = baseType.BaseType;
                }
            }
            catch
            {
                // Type hierarchy walk can fail
            }

            return false;
        }

        public static HashSet<ulong> BuildStaticRootAddresses(ClrRuntime runtime, bool includeSystemTypes = false)
        {
            var staticAddresses = new HashSet<ulong>();
            var processedTypes = new HashSet<ulong>();

            try
            {
                foreach (var obj in runtime.Heap.EnumerateObjects())
                {
                    var type = obj.Type;
                    if (type == null || !processedTypes.Add(type.MethodTable))
                    {
                        continue;
                    }

                    try
                    {
                        if (!includeSystemTypes && IsSystemType(type))
                        {
                            continue;
                        }

                        // type.StaticFields can hang indefinitely on certain types
                        // (e.g. MemoryRange) due to ClrMD metadata resolution.
                        // Use a CancellationTokenSource with timeout to abort the wait.
                        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
                        {
                            var token = cts.Token;
                            var fieldTask = Task.Run(() => type.StaticFields, token);

                            try
                            {
                                fieldTask.Wait(token);
                            }
                            catch (OperationCanceledException)
                            {
                                continue;
                            }

                            if (!fieldTask.IsCompleted || fieldTask.IsFaulted)
                            {
                                continue;
                            }

                            var staticFields = fieldTask.Result;

                            foreach (var staticField in staticFields)
                            {
                                if (!staticField.IsObjectReference)
                                {
                                    continue;
                                }

                                foreach (var domain in runtime.AppDomains)
                                {
                                    try
                                    {
                                        var staticObj = staticField.ReadObject(domain);
                                        if (staticObj.Address != 0)
                                        {
                                            staticAddresses.Add(staticObj.Address);
                                        }
                                    }
                                    catch
                                    {
                                        // Reading static field can fail
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Static field enumeration can fail for some types
                    }
                }
            }
            catch
            {
                // Heap walk can fail under contention
            }

            return staticAddresses;
        }


    }
}
