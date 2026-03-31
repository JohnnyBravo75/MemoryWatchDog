namespace MemoryWatchDog
{
    public class ThreadInfo
    {
        public string State { get; internal set; }
        public ulong Address { get; internal set; }
        public uint OSThreadId { get; internal set; }
        public int ManagedThreadId { get; internal set; }
    }

}
