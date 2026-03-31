namespace MemoryWatchDogApp
{
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using MemoryWatchDog;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ObservableCollection<ProcessInfo> allProcesses = new ObservableCollection<ProcessInfo>();
        private ICollectionView? processView;
        private string filterText = string.Empty;
        private bool dotNetOnlyFilter = false;
        private bool dotNetVersionsDetected = false;
        private ICollectionView? objectsView;
        private string objectsFilterText = string.Empty;
        private MemoryStats? currentStats;
        private CancellationTokenSource? captureCts;

        public MainWindow()
        {
            this.InitializeComponent();
            this.LoadProcesses();
        }

        private void LoadProcesses()
        {
            this.allProcesses.Clear();
            this.dotNetVersionsDetected = false;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    this.allProcesses.Add(new ProcessInfo
                    {
                        Id = proc.Id,
                        ProcessName = proc.ProcessName,
                        MemoryMB = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1),
                        MainWindowTitle = proc.MainWindowTitle
                    });
                }
                catch
                {
                    // Skip processes we cannot access
                }
            }

            var sorted = new ObservableCollection<ProcessInfo>(
                this.allProcesses.OrderBy(p => p.ProcessName));
            this.allProcesses = sorted;

            this.processView = CollectionViewSource.GetDefaultView(this.allProcesses);
            this.processView.Filter = this.ProcessFilter;
            this.ProcessGrid.ItemsSource = this.processView;

            this.StatusText.Text = $"{this.allProcesses.Count} processes loaded";

            if (this.dotNetOnlyFilter)
            {
                this.DetectDotNetVersionsAsync();
            }
        }

        private async void DetectDotNetVersionsAsync()
        {
            if (this.dotNetVersionsDetected)
            {
                this.processView?.Refresh();
                return;
            }

            this.StatusText.Text = "Detecting .NET processes...";
            this.DotNetOnlyCheckBox.IsEnabled = false;
            this.RefreshButton.IsEnabled = false;

            var watchDog = new MemoryWatchDog();
            var processList = this.allProcesses.ToList();

            var results = await Task.Run(() =>
            {
                var versions = new Dictionary<int, string>();
                foreach (var proc in processList)
                {
                    var version = watchDog.GetNETVersion(proc.Id);
                    if (!string.IsNullOrEmpty(version))
                    {
                        versions[proc.Id] = version;
                    }
                }
                return versions;
            });

            foreach (var proc in this.allProcesses)
            {
                if (results.TryGetValue(proc.Id, out var version))
                {
                    proc.NETVersion = version;
                }
            }

            this.dotNetVersionsDetected = true;

            this.processView?.Refresh();

            var dotNetCount = this.allProcesses.Count(p => p.IsDotNet);
            this.StatusText.Text = $"{dotNetCount} .NET processes found (of {this.allProcesses.Count} total)";
            this.DotNetOnlyCheckBox.IsEnabled = true;
            this.RefreshButton.IsEnabled = true;
        }

        private bool ProcessFilter(object item)
        {
            if (item is not ProcessInfo proc)
                return false;

            if (this.dotNetOnlyFilter && !proc.IsDotNet)
                return false;

            if (string.IsNullOrWhiteSpace(this.filterText))
                return true;

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

        private void DotNetOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            this.dotNetOnlyFilter = this.DotNetOnlyCheckBox.IsChecked == true;

            if (this.dotNetOnlyFilter && !this.dotNetVersionsDetected)
            {
                this.DetectDotNetVersionsAsync();
            }
            else
            {
                this.processView?.Refresh();
            }
        }

        private void ObjectsFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.objectsFilterText = this.ObjectsFilterTextBox.Text;
            this.objectsView?.Refresh();
        }

        private bool ObjectsFilter(object item)
        {
            if (string.IsNullOrWhiteSpace(this.objectsFilterText))
                return true;

            if (item is global::MemoryWatchDog.ObjectInfo obj)
            {
                return obj.TypeName.Contains(this.objectsFilterText, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.AttachButton.IsEnabled = this.ProcessGrid.SelectedItem is ProcessInfo;
        }

        private async void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.ProcessGrid.SelectedItem is not ProcessInfo selectedProcess)
            {
                return;
            }

            this.AttachButton.IsEnabled = false;
            this.RefreshButton.IsEnabled = false;
            this.ExportTxtButton.IsEnabled = false;
            this.ExportJsonButton.IsEnabled = false;
            this.CancelButton.IsEnabled = true;
            this.currentStats?.Clear();
            this.currentStats = null;
            this.StatusText.Text = $"Analyzing process {selectedProcess.ProcessName} (PID {selectedProcess.Id})...";
            this.OverviewText.Text = "Loading memory statistics, please wait...";
            this.ObjectsGrid.ItemsSource = null;
            this.ThreadsGrid.ItemsSource = null;

            this.CaptureProgressText.Text = "Objects: 0 | Types: 0";
            this.CaptureProgressPanel.Visibility = Visibility.Visible;

            this.captureCts = new CancellationTokenSource();
            var cancellationToken = this.captureCts.Token;

            var watchDog = new MemoryWatchDog();

            watchDog.CaptureProgress += (s, args) =>
            {
                this.Dispatcher.BeginInvoke(() =>
                {
                    this.CaptureProgressText.Text = $"Objects: {args.ObjectsProcessed} | Types: {args.TypesFound}";
                });
            };

            try
            {

                watchDog.CleanupMemory();

                // Filter
                var excludeSystemNs = this.ExcludeSystemNamespacesCheckBox.IsChecked == true;
                var filter = new MemoryStatsFilter
                {
                    ExcludeNameSpaces = excludeSystemNs
                        ? MemoryStatsFilter.GetSystemNamespaces()
                        : new List<string>(),
                    AggregateObjects = (this.AggregateObjectsCheckBox.IsChecked == true)
                };

                // Capture the stats
                var stats = await Task.Run(() =>
                    watchDog.GetMemoryStats(filter, selectedProcess.Id, cancellationToken));

                if (stats == null)
                {
                    this.OverviewText.Text = "Failed to retrieve memory statistics.";
                    this.StatusText.Text = "Error";
                    return;
                }

                this.DisplayMemoryStats(stats);
            }
            catch (OperationCanceledException)
            {
                this.OverviewText.Text = "Capture was cancelled.";
                this.StatusText.Text = "Cancelled";
            }
            catch (Exception ex)
            {
                this.OverviewText.Text = $"Error attaching to process:\n\n{ex.Message}\n\n" +
                    $"Note: You may need to run this application as Administrator to inspect other processes.\n" +
                    $"Only .NET processes can be fully analyzed.";
                this.StatusText.Text = "Error";
            }
            finally
            {
                this.AttachButton.IsEnabled = this.ProcessGrid.SelectedItem is ProcessInfo;
                this.RefreshButton.IsEnabled = true;
                this.CancelButton.IsEnabled = false;
                this.CaptureProgressPanel.Visibility = Visibility.Collapsed;

                this.captureCts?.Dispose();
                this.captureCts = null;
                watchDog?.Dispose();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.captureCts?.Cancel();
        }

        private void DisplayMemoryStats(MemoryStats stats)
        {
            if (stats == null)
            {
                stats = new MemoryStats();
            }

            if (stats != null)
            {
                this.currentStats = stats;
                this.ExportTxtButton.IsEnabled = true;
                this.ExportJsonButton.IsEnabled = true;

                this.StatsHeader.Text = $"Memory Statistics - {stats.ProcessName}  (PID {stats.ProcessId})";
                this.OverviewText.Text = stats.BuildOverviewStatsString();

                var objectsList = stats.Types.Values
                    .OrderByDescending(o => o.Size)
                    .ToList();
                this.ObjectsGrid.ItemsSource = objectsList;
                this.objectsView = CollectionViewSource.GetDefaultView(objectsList);
                this.objectsView.Filter = this.ObjectsFilter;
                this.ObjectsFilterTextBox.Text = string.Empty;

                this.ThreadsGrid.ItemsSource = stats.Threads;

                this.StatusText.Text = $"Done — {stats.ObjectCount} types, {stats.Threads.Count} threads";
            }
            else
            {
                this.currentStats = null;
                this.StatsHeader.Text = "";
                this.OverviewText.Text = "";
                this.ObjectsGrid.ItemsSource = null;
                this.ThreadsGrid.ItemsSource = null;
                this.StatusText.Text = "";
            }
        }

        private void ImportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var stats = MemoryStats.ReadFromFile(dialog.FileName);
                    if (stats == null)
                    {
                        MessageBox.Show(
                            "The file does not contain valid memory snapshot data.",
                            "Import Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    this.DisplayMemoryStats(stats);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to import snapshot:\n\n{ex.Message}",
                        "Import Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void ExportTxtButton_Click(object sender, RoutedEventArgs e)
        {
            this.ExportSnapshot(MemStatsFileFormats.txt, "Text Files (*.txt)|*.txt|All Files (*.*)|*.*", "txt");
        }

        private void ExportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            this.ExportSnapshot(MemStatsFileFormats.json, "JSON Files (*.json)|*.json|All Files (*.*)|*.*", "json");
        }

        private void ExportSnapshot(MemStatsFileFormats format, string filter, string defaultExt)
        {
            if (this.currentStats == null)
            {
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = $"MemorySnapshot.{defaultExt}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    this.currentStats.WriteToFile(dialog.FileName, format);
                    this.StatusText.Text = $"Exported to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to export snapshot:\n\n{ex.Message}",
                        "Export Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void ObjectsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (this.ObjectsGrid.SelectedItem is not TypeInfo typeInfo)
            {
                return;
            }

            if (typeInfo.Objects.Count == 0)
            {
                MessageBox.Show(
                    "Individual objects are not available in aggregate mode.\nUncheck 'Aggregate Objects' and take a new snapshot to view object details.",
                    "Aggregate Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var detailWindow = new ObjectDetailWindow(typeInfo);
            detailWindow.Owner = this;
            detailWindow.Show();
        }

    }
}