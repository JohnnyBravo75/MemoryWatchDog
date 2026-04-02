namespace MemoryWatchDog
{
    using System.Collections.Generic;
    using Microsoft.Diagnostics.Runtime;
    using Newtonsoft.Json;

    public class ObjectInfo
    {
        public string TypeName { get; set; } = "";

        public long Count { get; set; } = 1;

        public ulong Size { get; set; } = 0;

        public string ElementType { get; set; } = "";

        [JsonIgnore]
        public string AssemblyName { get; set; } = "";

        public ClrObject? Reference { get; set; }

        public string DisplayValue { get; set; } = "";

        public bool IsDisposed { get; set; }

        public List<ReferenceInfo> References { get; set; } = new List<ReferenceInfo>();

        public void Clear()
        {
            this.TypeName = null;
            this.ElementType = null;
            this.AssemblyName = null;
            this.Reference = null;
            if (this.References != null)
            {
                foreach (var reference in this.References)
                {
                    reference.Clear();
                }
                this.References.Clear();
            }
        }

        public override string ToString()
        {
            return $"Type: {TypeName}, Size: {Size}, ElementType: {ElementType}, Assembly: {AssemblyName}";
        }

        public TypeInfo GetTypeInfo()
        {
            return new TypeInfo
            {
                TypeName = this.TypeName,
                Count = this.Count,
                Size = this.Size,
                ElementType = this.ElementType,
                AssemblyName = this.AssemblyName
            };
        }
    }

}
