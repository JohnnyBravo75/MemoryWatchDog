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

        public long TotalSize { get; set; }

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

        [Description("Types grouping the objects by their type name")]
        public Dictionary<string, TypeInfo> Types { get; set; } = new Dictionary<string, TypeInfo>();

        public List<ThreadInfo> Threads { get; } = new List<ThreadInfo>();

        public string NETVersion { get; internal set; }


        public void Clear()
        {
            this.Threads?.Clear();

            if (this.Types != null)
            {
                foreach (var type in this.Types)
                {
                    if (type.Value.Objects != null)
                    {
                        foreach (var obj in type.Value.Objects)
                        {
                            obj.Reference = null;
                            obj.References?.Clear();
                        }

                        type.Value.Objects.Clear();
                    }
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
                $"Total Heap Size:                   {CommonUtil.FormatBytes(stats.TotalSize)}\n" +
                $"Total Collected Object Size:       {CommonUtil.FormatBytes(stats.TotalCollectedObjectSize)}\n" +
                $"Total Excluded Size:               {CommonUtil.FormatBytes(stats.TotalSize - stats.TotalCollectedObjectSize)}\n" +
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
