namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public class ThreadInfo
    {
        public string Name { get; internal set; }
        public string State { get; internal set; }
        public ulong Address { get; internal set; }
        public uint OSThreadId { get; internal set; }
        public int ManagedThreadId { get; internal set; }
        public bool IsBackground { get; internal set; }
        public bool IsThreadPoolThread { get; internal set; }
        public string CurrentExceptionType { get; internal set; }

        public List<string> StackFrames { get; internal set; } = new List<string>();

        [JsonIgnore]
        public string StackFramesAll
        {
            get { return string.Join(Environment.NewLine, StackFrames); }
        }
    }
}
