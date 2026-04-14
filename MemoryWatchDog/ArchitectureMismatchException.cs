namespace MemoryWatchDog
{
    using System;

    /// <summary>
    /// Thrown when the current process architecture (x86/x64) does not match
    /// the target process. ClrMD requires both to be identical.
    /// </summary>
    public class ArchitectureMismatchException : InvalidOperationException
    {
        public int TargetProcessId { get; }

        public string TargetProcessName { get; }

        public bool TargetIs64Bit { get; }

        public bool SelfIs64Bit { get; }

        public string TargetArch => TargetIs64Bit ? "x64" : "x86";

        public string SelfArch => SelfIs64Bit ? "x64" : "x86";

        public string RequiredArch => TargetArch;

        public ArchitectureMismatchException(int targetProcessId, string targetProcessName, bool selfIs64Bit, bool targetIs64Bit)
            : base($"Architecture mismatch: this process is {(selfIs64Bit ? "x64" : "x86")} but target " +
                   $"'{targetProcessName}' (pid={targetProcessId}) is {(targetIs64Bit ? "x64" : "x86")}. " +
                   $"ClrMD requires both processes to have the same architecture.")
        {
            this.TargetProcessId = targetProcessId;
            this.TargetProcessName = targetProcessName;
            this.SelfIs64Bit = selfIs64Bit;
            this.TargetIs64Bit = targetIs64Bit;
        }
    }
}
