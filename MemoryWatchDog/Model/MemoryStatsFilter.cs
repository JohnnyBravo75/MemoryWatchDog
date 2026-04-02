namespace MemoryWatchDog
{
    using System.Collections.Generic;


    public class MemoryStatsFilter
    {
        private List<string> excludeNameSpaces = new List<string>();
        private List<string> includeNameSpaces = new List<string>();

        public bool AggregateObjects { get; set; } = true;

        public bool CaptureDisplayValues { get; set; } = true;

        public MemoryStatsFilter()
        {
            this.ExcludeNameSpaces = GetSystemNamespaces();
        }

        public static List<string> GetSystemNamespaces()
        {
            return new List<string>() { "<>", "System", "Microsoft", "Windows", "mscorlib", "MS.", "Global", "Global Namespace", "<CppImplementationDetails>", "<CrtImplementationDetails>", "Internal." };
        }

        public List<string> ExcludeNameSpaces
        {
            get
            {
                if (this.excludeNameSpaces == null)
                {
                    this.excludeNameSpaces = new List<string>();
                }
                return this.excludeNameSpaces;
            }
            set { this.excludeNameSpaces = value; }
        }

        public List<string> IncludeNameSpaces
        {
            get
            {
                if (this.includeNameSpaces == null)
                {
                    this.includeNameSpaces = new List<string>();
                }
                return this.includeNameSpaces;
            }
            set { this.includeNameSpaces = value; }
        }

        public long MinObjectCount { get; set; } = 1;

        public bool IsInNamespace(string typeName, List<string> namespaces)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            foreach (var @namespace in namespaces)
            {
                if (typeName.StartsWith(@namespace))
                {
                    return true;
                }
            }

            return false;
        }
    }

}
