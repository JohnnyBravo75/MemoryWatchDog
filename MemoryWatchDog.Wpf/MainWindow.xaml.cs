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
    using System.Windows.Media;
    using System.Windows.Threading;
    using MemoryWatchDog;

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
        private ObservableCollection<SnapshotItem> snapshots = new ObservableCollection<SnapshotItem>();

        // Auto Watch mode fields
        private DispatcherTimer? autoWatchTimer;
        private bool isAutoWatching;
        private bool isCollectingAutoSnapshot;
        private bool isTakingLeakSnapshot;
        private List<MemoryStats> autoSnapshots = new List<MemoryStats>();
        private List<LeakCandidate> currentLeakCandidates = new List<LeakCandidate>();
        private LeakDetector leakDetector = new LeakDetector();
        private int autoSnapshotCount;
        private int cooldownRemaining;

        public MainWindow()
        {
            this.InitializeComponent();
            this.SnapshotsListBox.ItemsSource = this.snapshots;
        }

        private void SelectProcessButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProcessSelectionDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.SelectedProcess != null)
            {
                this.selectedProcess = dialog.SelectedProcess;
                dialog.SelectedProcess = null;
                this.SelectedProcessText.Text = $"{this.selectedProcess.ProcessName}  (PID {this.selectedProcess.Id})";
                this.AttachButton.IsEnabled = true;
                this.ForceGCButton.IsEnabled = true;
                this.StartAutoWatchButton.IsEnabled = true;
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

            if (item is ObjectInfo obj)
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
            //this.ExportTxtButton.IsEnabled = false;
            this.ExportJsonButton.IsEnabled = false;
            this.CancelButton.IsEnabled = true;
            this.currentStats = null;
            this.StatusText.Text = $"Analyzing process {selectedProcess.ProcessName} (PID {selectedProcess.Id})...";
            this.OverviewText.Text = "Loading memory statistics, please wait...";
            this.ObjectsGrid.ItemsSource = null;
            this.ThreadsGrid.ItemsSource = null;

            this.CaptureProgressText.Text = "Attaching...";
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
                this.CaptureProgressText.Text = "Running GC...";
                this.OverviewText.Text = "Running Garbage Collector, please wait...";

                // clean up myself
                watchDog.ForceGC();

                // clean up target
                await Task.Run(() => watchDog.ForceRemoteGC(selectedProcess.Id));

                // Filter
                var excludeSystemNs = this.ExcludeSystemNamespacesCheckBox.IsChecked == true;
                var filter = new MemoryStatsFilter
                {
                    ExcludeNameSpaces = excludeSystemNs
                        ? ClrReader.GetSystemNamespaces()
                        : new List<string>(),
                    AggregateObjects = (this.AggregateObjectsCheckBox.IsChecked == true),
                    CaptureDisplayValues = (this.CaptureDisplayValuesCheckBox.IsChecked == true)
                };

                this.OverviewText.Text = "Loading memory statistics, please wait...";

                // Capture the stats
                var stats = await Task.Run(() =>
                    watchDog.GetMemoryStats(filter, selectedProcess.Id, cancellationToken));

                if (stats == null)
                {
                    this.OverviewText.Text = "Failed to retrieve memory statistics.";
                    this.StatusText.Text = "Error";
                    return;
                }

                this.AddSnapshotAndSelect(stats);
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
                //this.ExportTxtButton.IsEnabled = true;
                this.ExportJsonButton.IsEnabled = true;

                this.StatsHeader.Text = $"Memory Statistics - {stats.ProcessName}  ({stats.CaptureDate})";
                this.OverviewText.Text = stats.BuildOverviewStatsString();

                var objectsList = stats.Types.Values
                    .OrderByDescending(o => o.Size)
                    .ToList();
                this.ObjectsGrid.ItemsSource = objectsList;
                this.objectsView = CollectionViewSource.GetDefaultView(objectsList);
                this.objectsView.Filter = this.ObjectsFilter;
                this.ObjectsFilterTextBox.Text = string.Empty;

                this.ThreadsGrid.ItemsSource = stats.Threads;

                // Potential Leaks: all disposed objects still in memory
                var disposedObjects = stats.Types.Values
                    .SelectMany(t => t.Objects)
                    .Where(o => o.IsDisposed)
                    .OrderByDescending(o => o.Size)
                    .Select(o => new PotentialLeakObjectItem(o))
                    .ToList();
                this.PotentialLeaksGrid.ItemsSource = disposedObjects;
                this.PotentialLeaksCountText.Text = disposedObjects.Count > 0 ? $"({disposedObjects.Count})" : "";

                this.StatusText.Text = $"Done — {stats.ObjectCount} types, {stats.Threads.Count} threads";
            }
            else
            {
                this.currentStats = null;
                this.StatsHeader.Text = "";
                this.OverviewText.Text = "";
                this.ObjectsGrid.ItemsSource = null;
                this.ThreadsGrid.ItemsSource = null;
                this.PotentialLeaksGrid.ItemsSource = null;
                this.PotentialLeaksCountText.Text = "";
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

                    this.AddSnapshotAndSelect(stats);
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

        // ======================== Auto Watch Mode ========================

        private void ModeTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != this.ModeTabControl)
            {
                return;
            }
        }

        private AutoModeSettings ReadAutoModeSettings()
        {
            var settings = new AutoModeSettings();

            if (int.TryParse(this.AutoIntervalTextBox.Text, out int interval) && interval >= 10 && interval <= 600)
            {
                settings.SnapshotIntervalSeconds = interval;
            }

            if (int.TryParse(this.AutoMaxSnapshotsTextBox.Text, out int maxSnap) && maxSnap >= 5 && maxSnap <= 100)
            {
                settings.MaxSnapshotsToKeep = maxSnap;
            }

            if (int.TryParse(this.AutoWarmupTextBox.Text, out int warmup) && warmup >= 2 && warmup <= 50)
            {
                settings.WarmupSnapshotCount = warmup;
            }

            if (int.TryParse(this.AutoMinGrowthTextBox.Text, out int minGrowth) && minGrowth >= 2 && minGrowth <= 50)
            {
                settings.MinConsecutiveGrowthCount = minGrowth;
            }

            settings.ForceGCBeforeSnapshot = this.AutoForceGCCheckBox.IsChecked == true;

            return settings;
        }

        private void StartAutoWatchButton_Click(object sender, RoutedEventArgs e)
        {
            this.StartAutoWatch();
        }

        private void StopAutoWatchButton_Click(object sender, RoutedEventArgs e)
        {
            this.StopAutoWatch();
        }

        private bool StartAutoWatch()
        {
            if (this.selectedProcess == null)
            {
                return false;
            }

            var settings = this.ReadAutoModeSettings();

            this.isAutoWatching = true;
            this.autoSnapshotCount = 0;
            this.cooldownRemaining = 0;
            this.autoSnapshots.Clear();
            this.currentLeakCandidates.Clear();
            this.leakDetector.Reset();
            this.MemoryGraph.Clear();
            this.LeakCandidatesGrid.ItemsSource = null;
            this.LeakCandidateCountText.Text = "";
            this.TakeLeakSnapshotButton.IsEnabled = false;
            this.ExportReportButton.IsEnabled = false;

            this.StartAutoWatchButton.IsEnabled = false;
            this.StopAutoWatchButton.IsEnabled = true;
            this.AttachButton.IsEnabled = false;
            this.SelectProcessButton.IsEnabled = false;
            this.AutoWatchStatusText.Text = $"Starting watch on {this.selectedProcess.ProcessName} (PID {this.selectedProcess.Id})...";

            // Disable settings editing while running
            this.AutoIntervalTextBox.IsEnabled = false;
            this.AutoMaxSnapshotsTextBox.IsEnabled = false;
            this.AutoWarmupTextBox.IsEnabled = false;
            this.AutoMinGrowthTextBox.IsEnabled = false;

            if (this.autoWatchTimer != null)
            {
                this.autoWatchTimer.Tick -= this.AutoWatchTimer_Tick;
            }

            this.autoWatchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(settings.SnapshotIntervalSeconds)
            };
            this.autoWatchTimer.Tick += this.AutoWatchTimer_Tick;

            // Take first snapshot immediately
            this.AutoWatchTimer_Tick(this, EventArgs.Empty);
            this.autoWatchTimer.Start();
            return true;
        }

        private async void AutoWatchTimer_Tick(object? sender, EventArgs e)
        {
            if (!this.isAutoWatching || this.selectedProcess == null || this.isCollectingAutoSnapshot)
            {
                return;
            }

            this.isCollectingAutoSnapshot = true;
            int processId = this.selectedProcess.Id;
            var settings = this.ReadAutoModeSettings();

            try
            {
                // Force GC before snapshot to reduce false positives (#3)
                using var watchDog = new MemoryWatchDog();
                if (settings.ForceGCBeforeSnapshot)
                {
                    try
                    {
                        await Task.Run(() => watchDog.ForceRemoteGC(processId));
                    }
                    catch
                    {
                        // GC trigger can fail, continue with snapshot anyway
                    }
                }

                // Take a lightweight aggregated snapshot (objects counted by type, no threads)
                var filter = new MemoryStatsFilter
                {
                    CaputureObjects = true,
                    AggregateObjects = true,
                    CaputureThreads = false,
                    CaptureDisplayValues = false
                };

                var snapshot = await Task.Run(() => watchDog.GetMemoryStats(filter, processId));

                if (!this.isAutoWatching)
                {
                    return;
                }

                this.autoSnapshotCount++;
                this.autoSnapshots.Add(snapshot);

                // Update memory graph
                this.MemoryGraph.AddSnapshot(snapshot);

                // Prune if needed
                LeakDetector.PruneSnapshots(this.autoSnapshots, settings.MaxSnapshotsToKeep);

                // Update status
                bool isWarmingUp = this.autoSnapshots.Count < settings.WarmupSnapshotCount;
                string phase = isWarmingUp
                    ? $"Warming up ({this.autoSnapshots.Count}/{settings.WarmupSnapshotCount})"
                    : "Analyzing";

                this.AutoWatchStatusText.Text =
                    $"{phase} — {this.selectedProcess?.ProcessName} (PID {processId}) — " +
                    $"Snapshot #{this.autoSnapshotCount} — {snapshot.CaptureDate.ToLocalTime():HH:mm:ss}";

                // Run analysis if past warmup and not in cooldown
                if (!isWarmingUp)
                {
                    if (this.cooldownRemaining > 0)
                    {
                        this.cooldownRemaining--;
                        this.AutoWatchStatusText.Text += $" — Cooldown ({this.cooldownRemaining} remaining)";
                    }
                    else
                    {
                        var candidates = this.leakDetector.Analyze(this.autoSnapshots, settings);
                        this.currentLeakCandidates = candidates;
                        this.LeakCandidatesGrid.ItemsSource = candidates;

                        if (candidates.Count > 0)
                        {
                            this.LeakCandidateCountText.Text = $"({candidates.Count})";
                            this.TakeLeakSnapshotButton.IsEnabled = true;
                            this.ExportReportButton.IsEnabled = true;
                            this.AutoWatchStatusText.Text += $" — 🔴 {candidates.Count} leak candidate(s) found";
                        }
                        else
                        {
                            this.LeakCandidateCountText.Text = "";
                            this.TakeLeakSnapshotButton.IsEnabled = false;
                        }
                    }
                }
            }
            catch
            {
                this.StopAutoWatch();
                this.AutoWatchStatusText.Text = "Process exited or became unavailable.";
            }
            finally
            {
                this.isCollectingAutoSnapshot = false;
            }
        }

        private void StopAutoWatch()
        {
            this.isAutoWatching = false;

            if (this.autoWatchTimer != null)
            {
                this.autoWatchTimer.Stop();
                this.autoWatchTimer.Tick -= this.AutoWatchTimer_Tick;
                this.autoWatchTimer = null;
            }

            this.StartAutoWatchButton.IsEnabled = this.selectedProcess != null;
            this.StopAutoWatchButton.IsEnabled = false;
            this.AttachButton.IsEnabled = this.selectedProcess != null;
            this.SelectProcessButton.IsEnabled = true;

            // Re-enable settings editing
            this.AutoIntervalTextBox.IsEnabled = true;
            this.AutoMaxSnapshotsTextBox.IsEnabled = true;
            this.AutoWarmupTextBox.IsEnabled = true;
            this.AutoMinGrowthTextBox.IsEnabled = true;

            if (string.IsNullOrEmpty(this.AutoWatchStatusText.Text) ||
                !this.AutoWatchStatusText.Text.Contains("unavailable"))
            {
                this.AutoWatchStatusText.Text += " — Stopped.";
            }
        }

        private async void TakeLeakSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.selectedProcess == null || this.currentLeakCandidates.Count == 0 || this.isTakingLeakSnapshot)
            {
                return;
            }

            this.isTakingLeakSnapshot = true;
            this.TakeLeakSnapshotButton.IsEnabled = false;
            int processId = this.selectedProcess.Id;
            var suspectTypeNames = this.currentLeakCandidates.Select(c => c.TypeName).ToList();

            this.AutoWatchStatusText.Text += " — Taking detailed snapshot of suspects...";

            try
            {
                using var watchDog = new MemoryWatchDog();
                var filter = new MemoryStatsFilter
                {
                    CaputureObjects = true,
                    AggregateObjects = false,
                    CaputureThreads = false,
                    CaptureDisplayValues = true,
                    IncludeTypeNames = suspectTypeNames,
                    ExcludeNameSpaces = new List<string>()
                };

                var stats = await Task.Run(() => watchDog.GetMemoryStats(filter, processId));

                if (stats != null)
                {
                    // Cross-reference disposed objects (#4)
                    this.leakDetector.CrossReferenceDisposedObjects(this.currentLeakCandidates, stats);
                    this.LeakCandidatesGrid.ItemsSource = null;
                    this.LeakCandidatesGrid.ItemsSource = this.currentLeakCandidates;

                    this.AddSnapshotAndSelect(stats, isAutoSnapshot: true);
                    this.AutoWatchStatusText.Text =
                        $"Detailed snapshot captured — {stats.ObjectCount} types, {stats.Types.Values.Sum(t => t.Count)} objects";

                    // Enter cooldown
                    var settings = this.ReadAutoModeSettings();
                    this.cooldownRemaining = settings.CooldownIntervalsAfterCapture;
                }
            }
            catch (Exception ex)
            {
                this.AutoWatchStatusText.Text = $"Failed to take detailed snapshot: {ex.Message}";
            }
            finally
            {
                this.isTakingLeakSnapshot = false;
                this.TakeLeakSnapshotButton.IsEnabled = this.currentLeakCandidates.Count > 0;
            }
        }

        private void ExportReportButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = this.ReadAutoModeSettings();
            var report = LeakReport.Build(
                this.autoSnapshots,
                this.currentLeakCandidates,
                settings,
                this.autoSnapshotCount);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"LeakReport_{report.ProcessName}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        System.IO.File.WriteAllText(dialog.FileName, report.BuildSummary());
                    }
                    else
                    {
                        report.WriteToFile(dialog.FileName);
                    }

                    this.AutoWatchStatusText.Text = $"Report exported to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to export report:\n\n{ex.Message}",
                        "Export Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void PotentialLeaksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (this.PotentialLeaksGrid.SelectedItem is not PotentialLeakObjectItem item)
            {
                return;
            }

            var syntheticType = new TypeInfo { TypeName = item.ObjectInfo.TypeName };
            syntheticType.AddObject(item.ObjectInfo);

            var detailWindow = new ObjectDetailWindow(syntheticType, this.currentStats);
            detailWindow.Owner = this;
            detailWindow.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            this.StopAutoWatch();
            base.OnClosed(e);
        }

        private void AddSnapshotAndSelect(MemoryStats stats, bool isAutoSnapshot = false)
        {
            var item = new SnapshotItem(stats, isAutoSnapshot);
            this.snapshots.Add(item);
            this.SnapshotsListBox.SelectedItem = item;
        }

        private void SnapshotsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItems = this.SnapshotsListBox.SelectedItems;
            int count = selectedItems.Count;

            this.RemoveSnapshotButton.IsEnabled = count > 0;
            this.CompareSnapshotsButton.IsEnabled = count == 2;

            if (count == 1 && selectedItems[0] is SnapshotItem item)
            {
                this.DisplayMemoryStats(item.Stats);
            }
        }

        private void CompareSnapshotsButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = this.SnapshotsListBox.SelectedItems;
            if (selectedItems.Count != 2)
            {
                return;
            }

            var itemA = (SnapshotItem)selectedItems[0]!;
            var itemB = (SnapshotItem)selectedItems[1]!;

            this.CompareSnapshots(itemA, itemB);
        }

        private void CompareSnapshots(SnapshotItem itemA, SnapshotItem itemB)
        {
            // Ensure older snapshot is A, newer is B
            MemoryStats statsA, statsB;
            if (itemA.Stats.CaptureDate <= itemB.Stats.CaptureDate)
            {
                statsA = itemA.Stats;
                statsB = itemB.Stats;
            }
            else
            {
                statsA = itemB.Stats;
                statsB = itemA.Stats;
            }

            var comparisonWindow = new SnapshotComparisonWindow(statsA, statsB);
            comparisonWindow.Owner = this;
            comparisonWindow.Show();
        }

        private void RemoveSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.SnapshotsListBox.SelectedItem is SnapshotItem snapshotItem)
            {
                this.RemoveSnapshot(snapshotItem);
            }
        }

        private void RemoveSnapshot(SnapshotItem item)
        {
            int index = this.snapshots.IndexOf(item);
            item.Stats?.Clear();
            item.Stats = null;
            this.snapshots.Remove(item);

            if (this.snapshots.Count > 0)
            {
                this.SnapshotsListBox.SelectedIndex = Math.Min(index, this.snapshots.Count - 1);
            }
            else
            {
                this.DisplayMemoryStats(null!);
                this.RemoveSnapshotButton.IsEnabled = false;
            }
        }

        private class SnapshotItem
        {
            private static readonly SolidColorBrush ManualBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99));
            private static readonly SolidColorBrush AutoBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

            public MemoryStats? Stats { get; set; }
            public string DisplayDate { get; set; }
            public string DisplayProcess { get; set; }
            public bool IsAutoSnapshot { get; set; }
            public string ModeTag { get; set; }
            public SolidColorBrush ModeTagBrush { get; set; }

            public SnapshotItem(MemoryStats stats, bool isAutoSnapshot = false)
            {
                this.Stats = stats;
                this.IsAutoSnapshot = isAutoSnapshot;
                this.DisplayDate = stats.CaptureDate.ToString("yyyy-MM-dd HH:mm:ss");
                this.DisplayProcess = $"{stats.ProcessName} (PID {stats.ProcessId})";
                this.ModeTag = isAutoSnapshot ? "Auto" : "Manual";
                this.ModeTagBrush = isAutoSnapshot ? AutoBrush : ManualBrush;
            }
        }

        private class PotentialLeakObjectItem
        {
            public string LeakReason
            {
                get
                {
                    List<string> reasons = new List<string>();
                    if (this.IsStatic) reasons.Add("static");
                    if (this.IsEventHandler) reasons.Add("event (not released)");
                    if (this.IsDisposed == true) reasons.Add("disposed (but in memory)");
                    return string.Join(", ", reasons);
                }
            }

            public ObjectInfo? ObjectInfo { get; set; }
            public string TypeName { get; }
            public ulong Size { get; }
            public string DisplayValue { get; }
            public int ReferenceCount { get; }
            public string ElementType { get; }
            public string AssemblyName { get; }
            public bool IsStatic { get; }
            public bool IsEventHandler { get; }
            public bool IsDisposed { get; }

            public PotentialLeakObjectItem(ObjectInfo obj)
            {
                this.ObjectInfo = obj;
                this.TypeName = obj.TypeName;
                this.Size = obj.Size;
                this.DisplayValue = obj.DisplayValue;
                this.ReferenceCount = obj.References.Count;
                this.ElementType = obj.ElementType;
                this.AssemblyName = obj.AssemblyName;
                this.IsStatic = obj.IsStatic;
                this.IsEventHandler = obj.IsEventHandler;
                this.IsDisposed = obj.IsDisposed;
            }
        }

    }
}