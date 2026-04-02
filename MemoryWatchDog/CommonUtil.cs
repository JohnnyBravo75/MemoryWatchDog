namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static class CommonUtil
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        public static string GetNamespaceFromTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return "Unknown Namespace";
            }

            int lastDotIndex = typeName.LastIndexOf('.');
            if (lastDotIndex > 0)
            {
                return typeName.Substring(0, lastDotIndex);  // Extract namespace (should be done by a regex, this approach is not complete)
            }
            else
            {
                return "Global Namespace";  // No dot found, global namespace
            }
        }

    }
}


