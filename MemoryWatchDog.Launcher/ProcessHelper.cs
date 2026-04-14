namespace MemoryWatchDog.Launcher
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
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
    }
}
