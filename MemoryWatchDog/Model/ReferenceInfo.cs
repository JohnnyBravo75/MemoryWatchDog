namespace MemoryWatchDog
{
    using System;

    public class ReferenceInfo
    {
        public string TypeName { get; set; } = "";

        public ulong Address { get; set; }

        public ulong Size { get; set; }

        public bool IsDisposed { get; set; }

        public string DisplayValue { get; set; } = "";

        public override string ToString()
        {
            return $"{TypeName} (0x{Address:X}, {Size} bytes)";
        }

        public void Clear()
        {
            this.TypeName = null;
            this.Address = 0;
            this.Size = 0;
            this.DisplayValue = "";
        }
    }
}
