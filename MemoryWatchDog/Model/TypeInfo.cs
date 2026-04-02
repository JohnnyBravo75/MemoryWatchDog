namespace MemoryWatchDog
{
    using System.Collections.Generic;
    using System.Linq;
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

        public void Clear()
        {
            if (this.Objects != null)
            {
                foreach (var obj in this.Objects)
                {
                    obj.Clear();
                }
                this.Objects.Clear();
            }
        }
    }

}
