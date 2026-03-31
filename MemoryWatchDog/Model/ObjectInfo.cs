namespace MemoryWatchDog
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Diagnostics.Runtime;
    using Newtonsoft.Json;

    public class TypeInfo
    {
        private long objectCount = 1;
        private ulong size = 0;

        public string TypeName { get; set; } = "";

        public long Count
        {
            get
            {
                if (this.Objects.Count > 0)
                {
                    return this.Objects.Count;
                }
                return this.objectCount;
            }
            set
            {
                this.objectCount = value;
            }

        }

        public ulong Size
        {
            get
            {
                if (this.Objects.Count > 0)
                {
                    return this.Objects.Aggregate(0UL, (acc, x) => acc + x.Size);
                }
                return this.size;
            }
            set
            {
                this.size = value;
            }
        }
        public string ElementType { get; set; } = "";

        [JsonIgnore]
        public string AssemblyName { get; set; } = "";

        public List<ObjectInfo> Objects { get; } = new List<ObjectInfo>();

        public override string ToString()
        {
            return $"Type: {TypeName}, Size: {Size}, ElementType: {ElementType}, Assembly: {AssemblyName}";
        }

        public void AddObject(ObjectInfo objectInfo, bool aggregate = true)
        {
            if (objectInfo == null || string.IsNullOrEmpty(objectInfo.TypeName))
            {
                return;
            }

            this.Objects.Add(objectInfo);

        }
    }

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

        public List<ReferenceInfo> References { get; set; } = new List<ReferenceInfo>();

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
