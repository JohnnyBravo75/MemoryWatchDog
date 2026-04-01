namespace MemoryWatchDogApp
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Shapes;
    using MemoryWatchDog;

    public partial class MemoryGraphControl : UserControl
    {
        private const int MaxPoints = 150;

        private readonly List<LiveMemorySnapshot> snapshots = new List<LiveMemorySnapshot>();
        private readonly ObservableCollection<LegendItem> legendItems = new ObservableCollection<LegendItem>();

        private static readonly SeriesInfo[] AllSeries = new[]
        {
            new SeriesInfo("Working Set", Colors.DodgerBlue),
            new SeriesInfo("Private Bytes", Colors.Teal),
            new SeriesInfo("GC Heap", Colors.Red),
            new SeriesInfo("Gen 0", Colors.LimeGreen),
            new SeriesInfo("Gen 1", Colors.Orange),
            new SeriesInfo("Gen 2", Colors.MediumPurple),
            new SeriesInfo("LOH", Colors.Magenta),
            new SeriesInfo("POH", Colors.SaddleBrown),
        };

        public MemoryGraphControl()
        {
            this.InitializeComponent();

            foreach (var s in AllSeries)
            {
                this.legendItems.Add(new LegendItem
                {
                    ColorBrush = new SolidColorBrush(s.Color),
                    Label = $"{s.Name}: --"
                });
            }

            this.LegendList.ItemsSource = this.legendItems;
        }

        public void Clear()
        {
            this.snapshots.Clear();
            this.GraphCanvas.Children.Clear();

            for (int i = 0; i < AllSeries.Length; i++)
            {
                this.legendItems[i].Label = $"{AllSeries[i].Name}: --";
            }
        }

        public void AddSnapshot(LiveMemorySnapshot snapshot)
        {
            this.snapshots.Add(snapshot);
            if (this.snapshots.Count > MaxPoints)
            {
                this.snapshots.RemoveAt(0);
            }

            this.Redraw();
        }

        private void Redraw()
        {
            this.GraphCanvas.Children.Clear();

            double w = this.GraphCanvas.ActualWidth;
            double h = this.GraphCanvas.ActualHeight;

            if (w < 20 || h < 20 || this.snapshots.Count < 2)
            {
                return;
            }

            // Precompute all values
            var allValues = new long[this.snapshots.Count][];
            for (int i = 0; i < this.snapshots.Count; i++)
            {
                allValues[i] = GetValues(this.snapshots[i]);
            }

            // Auto-scale Y axis
            long maxVal = 1;
            for (int i = 0; i < allValues.Length; i++)
            {
                for (int j = 0; j < allValues[i].Length; j++)
                {
                    if (allValues[i][j] > maxVal)
                    {
                        maxVal = allValues[i][j];
                    }
                }
            }

            maxVal = (long)(maxVal * 1.1);

            this.DrawGridLines(w, h, maxVal);

            // Draw each series as a polyline
            for (int si = 0; si < AllSeries.Length; si++)
            {
                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(AllSeries[si].Color),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };

                for (int i = 0; i < this.snapshots.Count; i++)
                {
                    double x = (i / (double)(MaxPoints - 1)) * w;
                    double y = h - (allValues[i][si] / (double)maxVal) * h;
                    polyline.Points.Add(new Point(x, y));
                }

                this.GraphCanvas.Children.Add(polyline);
            }

            // Update legend with latest values
            long[] latestVals = allValues[allValues.Length - 1];
            for (int i = 0; i < AllSeries.Length; i++)
            {
                this.legendItems[i].Label = $"{AllSeries[i].Name}: {FormatBytes(latestVals[i])}";
            }
        }

        private static long[] GetValues(LiveMemorySnapshot s)
        {
            return new long[]
            {
                s.WorkingSet,
                s.PrivateBytes,
                s.GCHeapSize,
                s.Gen0Size,
                s.Gen1Size,
                s.Gen2Size,
                s.LOHSize,
                s.POHSize,
            };
        }

        private void DrawGridLines(double w, double h, long maxVal)
        {
            int gridLines = 4;
            for (int i = 0; i <= gridLines; i++)
            {
                double y = h - (i / (double)gridLines) * h;
                long val = (long)(((double)i / gridLines) * maxVal);

                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = w,
                    Y2 = y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection(new[] { 4.0, 2.0 }),
                    IsHitTestVisible = false
                };
                this.GraphCanvas.Children.Add(line);

                if (i > 0)
                {
                    var label = new TextBlock
                    {
                        Text = FormatBytes(val),
                        FontSize = 9,
                        Foreground = Brushes.Gray,
                        FontFamily = new FontFamily("Consolas")
                    };
                    Canvas.SetLeft(label, 3);
                    Canvas.SetTop(label, y - 14);
                    this.GraphCanvas.Children.Add(label);
                }
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int idx = 0;
            double val = bytes;
            while (val >= 1024 && idx < units.Length - 1)
            {
                val /= 1024;
                idx++;
            }

            return $"{val:0.#} {units[idx]}";
        }

        private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.Redraw();
        }

        private class SeriesInfo
        {
            public SeriesInfo(string name, Color color)
            {
                this.Name = name;
                this.Color = color;
            }

            public string Name { get; }

            public Color Color { get; }
        }

        public class LegendItem : INotifyPropertyChanged
        {
            private string label = string.Empty;

            public SolidColorBrush? ColorBrush { get; set; }

            public string Label
            {
                get => this.label;
                set
                {
                    this.label = value;
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Label)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
