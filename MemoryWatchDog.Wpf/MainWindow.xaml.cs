namespace MemoryWatchDogApp
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using System.Windows.Threading;
    using MemoryWatchDog;
    using Microsoft.Diagnostics.Runtime;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ProcessInfo? selectedProcess;
        private ICollectionView? objectsView;
        private string objectsFilterText = string.Empty;
        private MemoryStats? currentStats;
        private CancellationTokenSource? captureCts;
        private DispatcherTimer? profilingTimer;
        private bool isProfiling;
        private bool isCollectingSnapshot;

        public MainWindow()
        {
            this.InitializeComponent();
        }

        private void SelectProcessButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProcessSelectionDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.SelectedProcess != null)
            {
                this.selectedProcess = dialog.SelectedProcess;
                this.SelectedProcessText.Text = $"{this.selectedProcess.ProcessName}  (PID {this.selectedProcess.Id})";
                this.AttachButton.IsEnabled = true;
                this.ForceGCButton.IsEnabled = true;
                this.StartProfilingButton.IsEnabled = true;
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

        private async void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.selectedProcess == null)
            {
                return;
            }

            var selectedProcess = this.selectedProcess;

            this.AttachButton.IsEnabled = false;
            this.SelectProcessButton.IsEnabled = false;
            this.ExportTxtButton.IsEnabled = false;
            this.ExportJsonButton.IsEnabled = false;
            this.CancelButton.IsEnabled = true;
            this.StartProfilingButton.IsEnabled = false;
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
                this.AttachButton.IsEnabled = this.selectedProcess != null;
                this.SelectProcessButton.IsEnabled = true;
                this.CancelButton.IsEnabled = false;
                this.StartProfilingButton.IsEnabled = this.selectedProcess != null;
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

        private async void ForceGCButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.selectedProcess == null)
            {
                return;
            }

            var process = this.selectedProcess;

            this.ForceGCButton.IsEnabled = false;
            this.StatusText.Text = $"Forcing GC on process {process.ProcessName} (PID {process.Id})...";

            try
            {
                using (var watchDog = new MemoryWatchDog())
                {
                    await Task.Run(() => watchDog.ForceRemoteGC(process.Id));
                }

                this.StatusText.Text = $"GC triggered on process {process.ProcessName} (PID {process.Id})";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to force GC:\n\n{ex.Message}\n\n" +
                    $"Note: This requires the target to be a .NET process and may require Administrator privileges.",
                    "Force GC Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                this.StatusText.Text = "Force GC failed";
            }
            finally
            {
                this.ForceGCButton.IsEnabled = this.selectedProcess != null;
            }
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

            var detailWindow = new ObjectDetailWindow(typeInfo, this.currentStats);
            detailWindow.Owner = this;
            detailWindow.Show();
        }

        private void StartProfilingButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.selectedProcess == null)
            {
                return;
            }

            this.isProfiling = true;
            this.ProfilingPanel.Visibility = Visibility.Visible;
            this.MemoryGraph.Clear();
            this.StartProfilingButton.IsEnabled = false;
            this.StopProfilingButton.IsEnabled = true;
            this.AttachButton.IsEnabled = false;
            this.SelectProcessButton.IsEnabled = false;
            this.ProfilingStatusText.Text = $"Profiling {this.selectedProcess.ProcessName} (PID {this.selectedProcess.Id})...";

            this.profilingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            this.profilingTimer.Tick += this.ProfilingTimer_Tick;

            // Take first snapshot immediately
            this.ProfilingTimer_Tick(this, EventArgs.Empty);
            this.profilingTimer.Start();
        }

        private async void ProfilingTimer_Tick(object? sender, EventArgs e)
        {
            if (!this.isProfiling || this.selectedProcess == null || this.isCollectingSnapshot)
            {
                return;
            }

            this.isCollectingSnapshot = true;
            int processId = this.selectedProcess.Id;

            try
            {
                var snapshot = await Task.Run(() => ClrUtil.CaptureLiveSnapshot(processId));
                if (this.isProfiling)
                {
                    this.MemoryGraph.AddSnapshot(snapshot);
                    this.ProfilingStatusText.Text =
                        $"Profiling {this.selectedProcess?.ProcessName} (PID {processId}) — {snapshot.Timestamp:HH:mm:ss}";
                }
            }
            catch
            {
                this.StopProfiling();
                this.ProfilingStatusText.Text = "Process exited or became unavailable.";
            }
            finally
            {
                this.isCollectingSnapshot = false;
            }
        }


        private void StopProfilingButton_Click(object sender, RoutedEventArgs e)
        {
            this.StopProfiling();
        }

        private void StopProfiling()
        {
            this.isProfiling = false;

            if (this.profilingTimer != null)
            {
                this.profilingTimer.Stop();
                this.profilingTimer.Tick -= this.ProfilingTimer_Tick;
                this.profilingTimer = null;
            }

            this.StartProfilingButton.IsEnabled = this.selectedProcess != null;
            this.StopProfilingButton.IsEnabled = false;
            this.AttachButton.IsEnabled = this.selectedProcess != null;
            this.SelectProcessButton.IsEnabled = true;

            if (string.IsNullOrEmpty(this.ProfilingStatusText.Text) ||
                !this.ProfilingStatusText.Text.Contains("unavailable"))
            {
                this.ProfilingStatusText.Text = "Profiling stopped.";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            this.StopProfiling();
            base.OnClosed(e);
        }

    }
}