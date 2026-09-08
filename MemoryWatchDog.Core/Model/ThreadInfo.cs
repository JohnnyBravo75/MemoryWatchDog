namespace MemoryWatchDog.Core
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public class ThreadInfo
    {
        public string Name { get; set; }
        public string State { get; set; }
        public ulong Address { get; set; }
        public uint OSThreadId { get; set; }
        public int ManagedThreadId { get; set; }
        public bool IsBackground { get; set; }
        public bool IsThreadPoolThread { get; set; }
        public string CurrentExceptionType { get; set; }

        public List<string> StackFrames { get; set; } = new List<string>();

        [JsonIgnore]
        public string StackFramesAll
        {
            get { return string.Join(Environment.NewLine, StackFrames); }
        }
    }
}
