namespace MemoryWatchDog
{
    using System;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Helpers for detecting and resolving architecture mismatches between
    /// the current process and a target process.
    /// </summary>
    public static class ArchitectureHelper
    {
        /// <summary>
        /// Resolves the path to the sibling exe for the opposite architecture.
        /// Expects the deployment layout:
        ///   install-dir\x64\MemoryWatchDog.Wpf.exe
        ///   install-dir\x86\MemoryWatchDog.Wpf.exe
        /// </summary>
        public static string FindSiblingExe(string requiredArch)
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe))
            {
                return null;
            }

            string currentDir = Path.GetDirectoryName(currentExe);
            string parentDir = Path.GetDirectoryName(currentDir);
            string exeName = Path.GetFileName(currentExe);

            if (parentDir == null)
            {
                return null;
            }

            // Try sibling folder: ..\<requiredArch>\<exeName>
            string siblingPath = Path.Combine(parentDir, requiredArch, exeName);
            if (File.Exists(siblingPath))
            {
                return siblingPath;
            }

            return null;
        }

        /// <summary>
        /// Launches the sibling exe with -attach &lt;pid&gt; to transparently
        /// hand off to the correct architecture build.
        /// Returns true if the sibling was found and launched.
        /// </summary>
        public static bool TryRelaunchForArchitecture(ArchitectureMismatchException mismatch)
        {
            string siblingExe = FindSiblingExe(mismatch.RequiredArch);
            if (siblingExe == null)
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = siblingExe,
                Arguments = $"-attach {mismatch.TargetProcessId}",
                UseShellExecute = true
            };

            Process.Start(startInfo);
            return true;
        }
    }
}
