namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Microsoft.Diagnostics.Runtime;
    using Newtonsoft.Json;


    public class MemoryStats
    {
        public DateTime CaptureDate { get; set; }

        public int ProcessId { get; set; }

        public string ProcessName { get; set; }

        [Description("Total size of the heap")]
        public long TotalSize { get; set; }

        [Description("Total size of the collected objects in the heap")]
        public long TotalCollectedObjectSize
        {
            get { return this.Types.Sum(o => (long)o.Value.Size); }
        }

        public long ObjectCount
        {
            get { return this.Types.Sum(o => o.Value.Count); }
        }

        public long ActiveWorkerThreads { get; set; }

        public long MaxThreads { get; set; }

        public int CpuUtilizationPercent { get; set; }

        public int IdleWorkerThreads { get; set; }

        public int WindowsThreadPoolThreadCount { get; set; }

        public long WorkingSet { get; set; }

        [Description("Size of the private bytes")]
        public long PrivateBytes { get; set; }

        [Description("Size of the GC heap")]
        public long GCHeapSize { get; set; }

        [Description("Size of the objects in generation 0 (Gen0)")]
        public long Gen0Size { get; set; }

        [Description("Size of the objects in generation 1 (Gen1)")]
        public long Gen1Size { get; set; }

        [Description("Size of the objects in generation 2 (Gen2)")]
        public long Gen2Size { get; set; }

        [Description("Size of the objects in the large object heap (LOH)")]
        public long LOHSize { get; set; }

        [Description("Size of the objects in the pinned object heap (POH)")]
        public long POHSize { get; set; }

        [Description("Types grouping the objects by their type name")]
        public Dictionary<string, TypeInfo> Types { get; set; } = new Dictionary<string, TypeInfo>();

        [Description("Threads in the process")]
        public List<ThreadInfo> Threads { get; } = new List<ThreadInfo>();

        [Description("The .NET version of the process")]
        public string NETVersion { get; internal set; }


        public void Clear()
        {
            this.Threads?.Clear();

            if (this.Types != null)
            {
                foreach (var type in this.Types)
                {
                    type.Value.Clear();
                }
                this.Types.Clear();
            }
        }

        public void AddObject(ObjectInfo objectInfo, bool aggregate = true)
        {
            if (objectInfo == null || string.IsNullOrEmpty(objectInfo.TypeName))
            {
                return;
            }

            // find the type info for the object type, if not exists create a new one and add it to the collection
            this.Types.TryGetValue(objectInfo.TypeName, out var currentTypeInfo);
            if (currentTypeInfo == null)
            {
                currentTypeInfo = objectInfo.GetTypeInfo();
                this.Types.Add(objectInfo.TypeName, currentTypeInfo);
            }


            if (aggregate)
            {
                currentTypeInfo.Count += objectInfo.Count;
                currentTypeInfo.Size += objectInfo.Size;
            }
            else
            {
                currentTypeInfo.AddObject(objectInfo, aggregate);
            }
        }



        public void AddThread(ThreadInfo threadInfo)
        {
            this.Threads.Add(threadInfo);
        }

        public void WriteToFile(string fileName = null, MemStatsFileFormats format = MemStatsFileFormats.txt)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = GetDefaultFilePath();
            }

            if (format == MemStatsFileFormats.json)
            {
                using (StreamWriter file = File.CreateText(fileName))
                {
                    var serializer = new JsonSerializer() { Formatting = Newtonsoft.Json.Formatting.Indented };
                    serializer.Serialize(file, this);
                }
            }
            else if (format == MemStatsFileFormats.txt)
            {
                using (StreamWriter file = File.CreateText(fileName))
                {
                    file.WriteLine(this.BuildOverviewStatsString());

                    file.WriteLine($"TypeName;TotalSize;Count;ElementType");
                    foreach (var obj in this.Types)
                    {
                        file.WriteLine($"{obj.Value.TypeName};{obj.Value.Size};{obj.Value.Count};{obj.Value.ElementType}");
                    }
                }
            }

        }

        public string BuildOverviewStatsString()
        {
            var stats = this;
            return
                $"Capture Date:                      {stats.CaptureDate}\n" +
                $"Process Name:                      {stats.ProcessName}\n" +
                $"Process Id:                        {stats.ProcessId}\n" +
                $".NET Version:                      {stats.NETVersion}\n" +
                $"\n" +
                $"Total memory:                      {CommonUtil.FormatBytes(stats.WorkingSet)}\n" +
                // $"Private Bytes:                     {CommonUtil.FormatBytes(stats.PrivateBytes)}\n" +
                $"\n" +
                $"Total Heap:                        {CommonUtil.FormatBytes(stats.TotalSize)}\n" +
                $"Total Collected Object:            {CommonUtil.FormatBytes(stats.TotalCollectedObjectSize)}\n" +
                $"Total Excluded:                    {CommonUtil.FormatBytes(stats.TotalSize - stats.TotalCollectedObjectSize)}\n" +
                $"\n" +
                $"GC Heap:                           {CommonUtil.FormatBytes(stats.GCHeapSize)}\n" +
                $"Gen 0:                             {CommonUtil.FormatBytes(stats.Gen0Size)}\n" +
                $"Gen 1:                             {CommonUtil.FormatBytes(stats.Gen1Size)}\n" +
                $"Gen 2:                             {CommonUtil.FormatBytes(stats.Gen2Size)}\n" +
                $"LOH:                               {CommonUtil.FormatBytes(stats.LOHSize)}\n" +
                $"POH:                               {CommonUtil.FormatBytes(stats.POHSize)}\n" +
                $"\n" +
                $"Threads Count:                     {stats.Threads.Count}\n" +
                $"Active Worker Threads:             {stats.ActiveWorkerThreads}\n" +
                $"Idle Worker Threads:               {stats.IdleWorkerThreads}\n" +
                $"\n" +
                $"Unique Object Types:               {stats.ObjectCount}\n";
        }

        public static MemoryStats ReadFromFile(string fileName)
        {
            using (StreamReader file = File.OpenText(fileName))
            {
                var serializer = new JsonSerializer();
                return (MemoryStats)serializer.Deserialize(file, typeof(MemoryStats));
            }
        }

        public static string GetDefaultFilePath()
        {
            string fileName;
            var execAsm = Assembly.GetExecutingAssembly();
            fileName = Path.Combine(Path.GetTempPath(), $"MemoryStats_{execAsm.GetName().Name}.dump");
            return fileName;
        }
    }

}
