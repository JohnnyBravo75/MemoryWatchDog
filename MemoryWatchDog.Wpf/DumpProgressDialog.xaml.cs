namespace MemoryWatchDogApp
{
    using System.Diagnostics;
    using System.IO;
    using System.Windows;

    public partial class DumpProgressDialog : Window
    {
        private Process? dumpProcess;
        private bool completed;

        public string? CreatedDumpFile { get; private set; }

        public DumpProgressDialog(string processName, int pid)
        {
            this.InitializeComponent();
            this.HeaderText.Text = $"Creating full memory dump for: {processName} (PID {pid})";
            this.Loaded += async (s, e) => await this.RunDumpAsync(processName, pid);
        }

        private async Task RunDumpAsync(string processName, int pid)
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "MemoryWatchDog");
            Directory.CreateDirectory(outputDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dumpFile = Path.Combine(outputDir, $"{processName}_{pid}_{timestamp}.dmp");

            this.AppendOutput($"Output directory: {outputDir}");
            this.AppendOutput($"Target file:      {dumpFile}");
            this.AppendOutput(string.Empty);

            // Try dotnet-dump first, fallback message if not found
            var args = $"collect -p {pid} -o \"{dumpFile}\"";
            this.AppendOutput($"> dotnet-dump {args}");
            this.AppendOutput(string.Empty);

            var psi = new ProcessStartInfo("dotnet-dump", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                this.dumpProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

                this.dumpProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        this.Dispatcher.BeginInvoke(() => this.AppendOutput(e.Data));
                };
                this.dumpProcess.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        this.Dispatcher.BeginInvoke(() => this.AppendOutput("ERR: " + e.Data));
                };

                this.dumpProcess.Start();
                this.dumpProcess.BeginOutputReadLine();
                this.dumpProcess.BeginErrorReadLine();

                await Task.Run(() => this.dumpProcess.WaitForExit());

                var exitCode = this.dumpProcess.ExitCode;
                this.completed = true;

                if (exitCode == 0 && File.Exists(dumpFile))
                {
                    var sizeMb = Math.Round(new FileInfo(dumpFile).Length / 1024.0 / 1024.0, 1);
                    this.ResultText.Text = $"✅ Dump created successfully ({sizeMb} MB)";
                    this.ResultText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                    this.CreatedDumpFile = dumpFile;
                    this.OpenDumpButton.IsEnabled = true;
                }
                else
                {
                    this.ResultText.Text = $"❌ Dump failed (exit code {exitCode}). Is 'dotnet-dump' installed? Run: dotnet tool install --global dotnet-dump";
                    this.ResultText.Foreground = System.Windows.Media.Brushes.DarkRed;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException)
            {
                this.AppendOutput("ERROR: dotnet-dump not found.");
                this.AppendOutput("Install it with: dotnet tool install --global dotnet-dump");
                this.ResultText.Text = "❌ dotnet-dump not found. Run: dotnet tool install --global dotnet-dump";
                this.ResultText.Foreground = System.Windows.Media.Brushes.DarkRed;
            }
            catch (Exception ex)
            {
                this.AppendOutput($"ERROR: {ex.Message}");
                this.ResultText.Text = $"❌ Error: {ex.Message}";
                this.ResultText.Foreground = System.Windows.Media.Brushes.DarkRed;
            }
            finally
            {
                this.dumpProcess?.Dispose();
                this.dumpProcess = null;
            }
        }

        private void AppendOutput(string line)
        {
            this.OutputTextBox.AppendText(line + "\n");
            this.OutputTextBox.ScrollToEnd();
        }

        private void OpenDumpButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (this.dumpProcess != null && !this.dumpProcess.HasExited)
            {
                try { this.dumpProcess.Kill(); } catch { }
            }
        }
    }
}
