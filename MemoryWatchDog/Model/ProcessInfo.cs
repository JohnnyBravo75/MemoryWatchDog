namespace MemoryWatchDogApp
{
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public double MemoryMB { get; set; }
        public string MainWindowTitle { get; set; } = string.Empty;
        public string NETVersion { get; set; } = string.Empty;
        public bool IsDotNet => !string.IsNullOrEmpty(NETVersion);
    }
}