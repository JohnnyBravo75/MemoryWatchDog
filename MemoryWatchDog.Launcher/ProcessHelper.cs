namespace MemoryWatchDog.WpfLauncher
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;

    public static class ProcessHelper
    {
        // ======================== Architecture Detection ========================

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr hProcess, out bool isWow64);

        public static string GetProcessArchitecture(Process process)
        {
            if (!Environment.Is64BitOperatingSystem)
            {
                return "x86";
            }

            try
            {
                IsWow64Process(process.Handle, out bool isWow64);
                return isWow64 ? "x86" : "x64";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// Finds the WPF exe for the given architecture.
        /// Checks multiple naming conventions and layouts.
        /// </summary>
        public static string? FindExe(string arch)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Subfolder layout: x64\MemoryWatchDog_x64.exe
            string subRenamed = Path.Combine(baseDir, arch, $"MemoryWatchDog_{arch}.exe");
            if (File.Exists(subRenamed))
            {
                return subRenamed;
            }

            // Subfolder layout: x64\MemoryWatchDog.Wpf.exe (legacy)
            string subPath = Path.Combine(baseDir, arch, "MemoryWatchDog.Wpf.exe");
            if (File.Exists(subPath))
            {
                return subPath;
            }

            // Flat layout: MemoryWatchDog_x64.exe
            string flatUnderscore = Path.Combine(baseDir, $"MemoryWatchDog_{arch}.exe");
            if (File.Exists(flatUnderscore))
            {
                return flatUnderscore;
            }

            // Flat layout: MemoryWatchDog.x64.exe
            string flatDot = Path.Combine(baseDir, $"MemoryWatchDog.{arch}.exe");
            if (File.Exists(flatDot))
            {
                return flatDot;
            }

            return null;
        }
    }
}
