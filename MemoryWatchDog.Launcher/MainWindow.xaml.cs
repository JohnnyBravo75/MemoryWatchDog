namespace MemoryWatchDog.Launcher
{
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;

    public partial class MainWindow : Window
    {
        private ObservableCollection<ProcessItem> allProcesses = new ObservableCollection<ProcessItem>();
        private ICollectionView? processView;
        private string filterText = string.Empty;

        public MainWindow()
        {
            this.InitializeComponent();
            this.LoadProcesses();
        }

        private void LoadProcesses()
        {
            this.allProcesses.Clear();

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    this.allProcesses.Add(new ProcessItem
                    {
                        Id = proc.Id,
                        ProcessName = proc.ProcessName,
                        MemoryMB = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1),
                        MainWindowTitle = proc.MainWindowTitle,
                        Architecture = ProcessHelper.GetProcessArchitecture(proc)
                    });
                }
                catch
                {
                    // Skip processes we cannot access
                }
            }

            var sorted = new ObservableCollection<ProcessItem>(
                this.allProcesses.OrderBy(p => p.ProcessName));
            this.allProcesses = sorted;

            this.processView = CollectionViewSource.GetDefaultView(this.allProcesses);
            this.processView.Filter = this.ProcessFilter;
            this.ProcessGrid.ItemsSource = this.processView;

            this.StatusText.Text = $"{this.allProcesses.Count} processes loaded";
        }

        private bool ProcessFilter(object item)
        {
            if (item is not ProcessItem proc)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(this.filterText))
            {
                return true;
            }

            return proc.ProcessName.Contains(this.filterText, StringComparison.OrdinalIgnoreCase)
                || proc.Id.ToString().Contains(this.filterText, StringComparison.OrdinalIgnoreCase)
                || (proc.MainWindowTitle?.Contains(this.filterText, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.filterText = this.FilterTextBox.Text;
            this.processView?.Refresh();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            this.LoadProcesses();
        }

        private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.AttachButton.IsEnabled = this.ProcessGrid.SelectedItem is ProcessItem;
        }

        private void ProcessGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (this.ProcessGrid.SelectedItem is ProcessItem)
            {
                this.LaunchForSelectedProcess();
            }
        }

        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            this.LaunchForSelectedProcess();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void LaunchForSelectedProcess()
        {
            if (this.ProcessGrid.SelectedItem is not ProcessItem selected)
            {
                return;
            }

            string requiredArch = selected.Architecture;

            // If architecture is unknown, default to the current process architecture
            if (requiredArch != "x64" && requiredArch != "x86")
            {
                requiredArch = Environment.Is64BitProcess ? "x64" : "x86";
            }

            string? wpfExe = FindWpfExe(requiredArch);

            if (wpfExe == null)
            {
                // Try the other architecture as fallback
                string fallbackArch = requiredArch == "x64" ? "x86" : "x64";
                wpfExe = FindWpfExe(fallbackArch);

                if (wpfExe != null)
                {
                    var result = MessageBox.Show(
                        $"The target process '{selected.ProcessName}' is {requiredArch}, " +
                        $"but only the {fallbackArch} build was found.\n\n" +
                        $"Launch anyway? (attach will fail due to architecture mismatch)",
                        "Architecture Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
            }

            if (wpfExe == null)
            {
                MessageBox.Show(
                    $"Could not find MemoryWatchDog.Wpf for {requiredArch}.\n\n" +
                    $"Expected one of:\n" +
                    $"  {requiredArch}\\MemoryWatchDog.Wpf.exe  (subfolder layout)\n" +
                    $"  MemoryWatchDog.{requiredArch}.exe  (flat layout)\n\n" +
                    $"relative to: {AppDomain.CurrentDomain.BaseDirectory}",
                    "Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = wpfExe,
                    Arguments = $"-attach {selected.Id}",
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to launch:\n{wpfExe}\n\n{ex.Message}",
                    "Launch Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Finds the WPF exe for the given architecture.
        /// Checks multiple naming conventions and layouts.
        /// </summary>
        private static string? FindWpfExe(string arch)
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

    public class ProcessItem
    {
        public int Id { get; set; }

        public string ProcessName { get; set; } = string.Empty;

        public double MemoryMB { get; set; }

        public string MainWindowTitle { get; set; } = string.Empty;

        public string Architecture { get; set; } = string.Empty;
    }
}
