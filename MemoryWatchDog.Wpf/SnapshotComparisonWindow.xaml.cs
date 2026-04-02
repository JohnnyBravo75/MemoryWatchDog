namespace MemoryWatchDogApp
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Documents;
    using System.Windows.Media;
    using MemoryWatchDog;

    public partial class SnapshotComparisonWindow : Window
    {
        private MemoryStats? statsA;
        private MemoryStats? statsB;
        private List<ComparisonRow>? allRows;
        private ICollectionView? comparisonView;
        private string filterText = string.Empty;

        public SnapshotComparisonWindow(MemoryStats statsA, MemoryStats statsB)
        {
            this.InitializeComponent();
            this.statsA = statsA;
            this.statsB = statsB;

            this.ComparisonGrid.SelectionChanged += this.ComparisonGrid_SelectionChanged;

            this.SnapshotAText.Text = $"Old:  {statsA.CaptureDate:yyyy-MM-dd HH:mm:ss}  —  {statsA.ProcessName} (PID {statsA.ProcessId})";

            this.allRows = BuildComparisonRows(statsA, statsB);
            this.ComparisonGrid.ItemsSource = this.allRows;
            this.comparisonView = CollectionViewSource.GetDefaultView(this.allRows);
            this.comparisonView.Filter = this.RowFilter;

            int added = this.allRows.Count(r => r.CountA == 0);
            int removed = this.allRows.Count(r => r.CountB == 0);
            int changed = this.allRows.Count(r => r.CountDiff != 0);
            this.StatusText.Text = $"{this.allRows.Count} types total — {changed} changed, {added} new, {removed} removed";

            this.BuildSnapshotBHeader(statsB);

            this.Closed += this.SnapshotComparisonWindow_Closed;
        }

        private void SnapshotComparisonWindow_Closed(object? sender, EventArgs e)
        {
            this.ComparisonGrid.SelectionChanged -= this.ComparisonGrid_SelectionChanged;
            this.Closed -= this.SnapshotComparisonWindow_Closed;

            this.statsA = null;
            this.statsB = null;
            this.allRows = null;

            this.Owner = null;
        }

        private void BuildSnapshotBHeader(MemoryStats statsB)
        {
            long totalCountDiff = this.allRows.Sum(r => r.CountDiff);
            long totalSizeDiff = this.allRows.Sum(r => r.SizeDiff);

            this.SnapshotBText.Inlines.Clear();
            this.SnapshotBText.Inlines.Add(new Run(
                $"New:  {statsB.CaptureDate:yyyy-MM-dd HH:mm:ss}  —  {statsB.ProcessName} (PID {statsB.ProcessId})    ")
            { Foreground = Brushes.DarkBlue });

            // Object count diff
            this.SnapshotBText.Inlines.Add(new Run("Objects: ") { Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold });
            string countText = totalCountDiff > 0 ? $"+{totalCountDiff}" : $"{totalCountDiff}";
            Brush countBrush = totalCountDiff > 0 ? Brushes.Red : totalCountDiff < 0 ? new SolidColorBrush(Color.FromRgb(0, 140, 0)) : Brushes.Gray;
            this.SnapshotBText.Inlines.Add(new Run(countText) { Foreground = countBrush, FontWeight = FontWeights.Bold });

            this.SnapshotBText.Inlines.Add(new Run("    "));

            // Size diff
            this.SnapshotBText.Inlines.Add(new Run("Size: ") { Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold });
            string sizeText = totalSizeDiff > 0
                ? $"+{CommonUtil.FormatBytes(totalSizeDiff)}"
                : totalSizeDiff < 0
                    ? $"-{CommonUtil.FormatBytes(System.Math.Abs(totalSizeDiff))}"
                    : "0";
            Brush sizeBrush = totalSizeDiff > 0 ? Brushes.Red : totalSizeDiff < 0 ? new SolidColorBrush(Color.FromRgb(0, 140, 0)) : Brushes.Gray;
            this.SnapshotBText.Inlines.Add(new Run(sizeText) { Foreground = sizeBrush, FontWeight = FontWeights.Bold });
        }

        private static List<ComparisonRow> BuildComparisonRows(MemoryStats statsA, MemoryStats statsB)
        {
            var allTypeNames = new HashSet<string>(statsA.Types.Keys);
            allTypeNames.UnionWith(statsB.Types.Keys);

            var rows = new List<ComparisonRow>(allTypeNames.Count);

            foreach (var typeName in allTypeNames)
            {
                statsA.Types.TryGetValue(typeName, out var typeA);
                statsB.Types.TryGetValue(typeName, out var typeB);

                rows.Add(new ComparisonRow
                {
                    TypeName = typeName,
                    CountA = typeA?.Count ?? 0,
                    CountB = typeB?.Count ?? 0,
                    SizeA = (long)(typeA?.Size ?? 0),
                    SizeB = (long)(typeB?.Size ?? 0),
                    ElementType = typeB?.ElementType ?? typeA?.ElementType ?? ""
                });
            }

            return rows.OrderByDescending(r => System.Math.Abs(r.CountDiff)).ToList();
        }

        private bool RowFilter(object item)
        {
            if (item is not ComparisonRow row)
                return false;

            if (this.HideUnchangedCheckBox.IsChecked == true && row.CountDiff == 0 && row.SizeDiff == 0)
                return false;

            if (!string.IsNullOrWhiteSpace(this.filterText) &&
                !row.TypeName.Contains(this.filterText, System.StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.filterText = this.FilterTextBox.Text;
            this.comparisonView?.Refresh();
        }

        private void HideUnchangedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            this.comparisonView?.Refresh();
        }

        private void ComparisonGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = this.ComparisonGrid.SelectedItem is ComparisonRow;
            this.DetailOldButton.IsEnabled = hasSelection;
            this.DetailNewButton.IsEnabled = hasSelection;
        }

        private void DetailOldButton_Click(object sender, RoutedEventArgs e)
        {
            this.OpenDetailWindow(this.statsA, "Old");
        }

        private void DetailNewButton_Click(object sender, RoutedEventArgs e)
        {
            this.OpenDetailWindow(this.statsB, "New");
        }

        private void OpenDetailWindow(MemoryStats stats, string label = "")
        {
            if (this.ComparisonGrid.SelectedItem is not ComparisonRow row)
            {
                return;
            }

            if (!stats.Types.TryGetValue(row.TypeName, out var typeInfo))
            {
                MessageBox.Show(
                    $"Type '{row.TypeName}' does not exist in the {label} snapshot.",
                    "Type Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var detailWindow = new ObjectDetailWindow(typeInfo, stats);
            detailWindow.Owner = this;
            detailWindow.Title = $"Object Details ({label}) — {row.TypeName}";
            detailWindow.Show();
        }

        internal class ComparisonRow
        {
            private static readonly Brush RedBrush = new SolidColorBrush(Colors.Red);
            private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0, 140, 0));
            private static readonly Brush GrayBrush = new SolidColorBrush(Colors.Gray);

            public string TypeName { get; set; } = "";
            public long CountA { get; set; }
            public long CountB { get; set; }
            public long SizeA { get; set; }
            public long SizeB { get; set; }
            public string ElementType { get; set; } = "";

            public long CountDiff => this.CountB - this.CountA;
            public long SizeDiff => this.SizeB - this.SizeA;

            public string CountDiffText => this.CountDiff > 0 ? $"+{this.CountDiff}" : this.CountDiff < 0 ? $"{this.CountDiff}" : "0";
            public Brush CountDiffBrush => this.CountDiff > 0 ? RedBrush : this.CountDiff < 0 ? GreenBrush : GrayBrush;

            public string SizeAText => CommonUtil.FormatBytes(this.SizeA);
            public string SizeBText => CommonUtil.FormatBytes(this.SizeB);
            public string SizeDiffText => this.SizeDiff > 0 ? $"+{CommonUtil.FormatBytes(this.SizeDiff)}" : this.SizeDiff < 0 ? $"-{CommonUtil.FormatBytes(System.Math.Abs(this.SizeDiff))}" : "0";
            public Brush SizeDiffBrush => this.SizeDiff > 0 ? RedBrush : this.SizeDiff < 0 ? GreenBrush : GrayBrush;
        }
    }
}
