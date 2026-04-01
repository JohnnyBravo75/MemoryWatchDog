namespace MemoryWatchDogApp
{
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using MemoryWatchDog;

    /// <summary>
    /// Dialog for selecting a process from the running processes list.
    /// </summary>
    public partial class ProcessSelectionDialog : Window
    {
        private ObservableCollection<ProcessInfo> allProcesses = new ObservableCollection<ProcessInfo>();
        private ICollectionView? processView;
        private string filterText = string.Empty;
        private bool dotNetOnlyFilter = false;
        private bool dotNetVersionsDetected = false;

        public ProcessInfo? SelectedProcess { get; private set; }

        public ProcessSelectionDialog()
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

        private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.OkButton.IsEnabled = this.ProcessGrid.SelectedItem is ProcessInfo;
        }

        private void ProcessGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (this.ProcessGrid.SelectedItem is ProcessInfo)
            {
                this.SelectedProcess = (ProcessInfo)this.ProcessGrid.SelectedItem;
                this.DialogResult = true;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.ProcessGrid.SelectedItem is ProcessInfo selected)
            {
                this.SelectedProcess = selected;
                this.DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
