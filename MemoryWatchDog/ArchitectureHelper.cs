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
        /// Supports two deployment layouts:
        ///   Subfolder:  install-dir\x64\MemoryWatchDog.Wpf.exe
        ///   Flat:       install-dir\MemoryWatchDog.x64.exe
        /// </summary>
        public static string FindSiblingExe(string requiredArch)
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe))
            {
                return null;
            }

            string currentDir = Path.GetDirectoryName(currentExe);

            // Flat layout: MemoryWatchDog_x64.exe in the same folder
            string flatUnderscore = Path.Combine(currentDir, $"MemoryWatchDog_{requiredArch}.exe");
            if (File.Exists(flatUnderscore))
            {
                return flatUnderscore;
            }

            // Flat layout: MemoryWatchDog.x64.exe in the same folder
            string flatDot = Path.Combine(currentDir, $"MemoryWatchDog.{requiredArch}.exe");
            if (File.Exists(flatDot))
            {
                return flatDot;
            }

            // Subfolder layout: ..\<requiredArch>\MemoryWatchDog_<arch>.exe
            string parentDir = Path.GetDirectoryName(currentDir);
            if (parentDir != null)
            {
                string siblingRenamed = Path.Combine(parentDir, requiredArch, $"MemoryWatchDog_{requiredArch}.exe");
                if (File.Exists(siblingRenamed))
                {
                    return siblingRenamed;
                }

                // Legacy: ..\<requiredArch>\<same exe name>
                string exeName = Path.GetFileName(currentExe);
                string siblingPath = Path.Combine(parentDir, requiredArch, exeName);
                if (File.Exists(siblingPath))
                {
                    return siblingPath;
                }
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
