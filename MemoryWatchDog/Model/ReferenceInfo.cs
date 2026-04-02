namespace MemoryWatchDog
{
    public class ReferenceInfo
    {
        public string TypeName { get; set; } = "";

        public ulong Address { get; set; }

        public ulong Size { get; set; }

        public bool IsDisposed { get; set; }

        public override string ToString()
        {
            return $"{TypeName} (0x{Address:X}, {Size} bytes)";
        }
    }
}
