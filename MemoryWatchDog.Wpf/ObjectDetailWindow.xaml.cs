namespace MemoryWatchDogApp
{
    using System;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Media.Effects;
    using System.Windows.Shapes;
    using MemoryWatchDog;

    /// <summary>
    /// Interaction logic for ObjectDetailWindow.xaml
    /// </summary>
    public partial class ObjectDetailWindow : Window
    {
        private readonly TypeInfo typeInfo;
        private readonly MemoryStats memoryStats;
        private readonly List<ObjectDisplayItem> allDisplayItems;

        public ObjectDetailWindow(TypeInfo typeInfo, MemoryStats memoryStats = null)
        {
            this.InitializeComponent();
            this.typeInfo = typeInfo;
            this.memoryStats = memoryStats;

            this.TypeNameHeader.Text = typeInfo.TypeName;
            this.ObjectCountText.Text = $"{typeInfo.Objects.Count} objects";
            this.Title = $"Object Details — {typeInfo.TypeName}";

            this.allDisplayItems = typeInfo.Objects
                .Select(o => new ObjectDisplayItem(o))
                .ToList();
            this.ApplyFilter();
        }

        private void FilterReferencedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            this.ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (this.allDisplayItems == null)
            {
                return;
            }

            var filtered = this.FilterReferencedCheckBox.IsChecked == true
                ? this.allDisplayItems.Where(i => i.ObjectInfo.References.Count > 0).ToList()
                : this.allDisplayItems;

            this.ObjectListBox.ItemsSource = filtered;
            this.ObjectCountText.Text = this.FilterReferencedCheckBox.IsChecked == true
                ? $"{filtered.Count} of {this.allDisplayItems.Count} objects (with references)"
                : $"{this.allDisplayItems.Count} objects";
        }

        private void ObjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.ObjectListBox.SelectedItem is ObjectDisplayItem item)
            {
                this.DrawDependencyGraph(item.ObjectInfo);
            }
            else
            {
                this.GraphCanvas.Children.Clear();
                this.NoSelectionText.Text = "Select an object from the list to view its dependencies.";
                this.NoSelectionText.Visibility = Visibility.Visible;
                this.GraphScrollViewer.Visibility = Visibility.Collapsed;
            }
        }

        private void DrawDependencyGraph(ObjectInfo obj)
        {
            this.GraphCanvas.Children.Clear();

            if (obj.References.Count == 0)
            {
                this.NoSelectionText.Text = "This object has no references.";
                this.NoSelectionText.Visibility = Visibility.Visible;
                this.GraphScrollViewer.Visibility = Visibility.Collapsed;
                return;
            }

            this.NoSelectionText.Visibility = Visibility.Collapsed;
            this.GraphScrollViewer.Visibility = Visibility.Visible;

            const double nodeWidth = 200;
            const double nodeHeight = 55;
            const double hGap = 25;
            const double vGap = 70;
            const double topMargin = 10;
            const int maxColumns = 4;

            int refCount = obj.References.Count;
            int columns = Math.Min(refCount, maxColumns);
            if (columns == 0)
            {
                columns = 1;
            }

            double totalRefWidth = columns * nodeWidth + (columns - 1) * hGap;
            double canvasWidth = Math.Max(nodeWidth + 40, totalRefWidth + 40);

            // Root node
            double rootX = canvasWidth / 2 - nodeWidth / 2;
            double rootY = topMargin;

            var rootNode = CreateNodeVisual(
                obj.TypeName,
                $"0x{obj.Reference?.Address:X} | {obj.Size} bytes",
                "#1565C0", "#E3F2FD", nodeWidth);
            Canvas.SetLeft(rootNode, rootX);
            Canvas.SetTop(rootNode, rootY);
            this.GraphCanvas.Children.Add(rootNode);

            double rootCenterX = rootX + nodeWidth / 2;
            double rootBottom = rootY + nodeHeight;

            // Reference nodes
            double refsStartY = rootBottom + vGap;
            double refsStartX = canvasWidth / 2 - totalRefWidth / 2;

            for (int i = 0; i < refCount; i++)
            {
                var refInfo = obj.References[i];
                int col = i % columns;
                int row = i / columns;

                double x = refsStartX + col * (nodeWidth + hGap);
                double y = refsStartY + row * (nodeHeight + vGap);

                var refNode = CreateNodeVisual(
                    refInfo.TypeName,
                    $"0x{refInfo.Address:X} | {refInfo.Size} bytes",
                    "#E65100", "#FFF3E0", nodeWidth);

                // Make reference node clickable to drill down
                if (this.memoryStats != null)
                {
                    refNode.Cursor = Cursors.Hand;
                    refNode.ToolTip = "Double-click to explore references";
                    var capturedRef = refInfo;
                    refNode.MouseLeftButtonDown += (s, ev) =>
                    {
                        if (ev.ClickCount == 2)
                        {
                            this.OpenReferenceDetail(capturedRef);
                            ev.Handled = true;
                        }
                    };
                }

                Canvas.SetLeft(refNode, x);
                Canvas.SetTop(refNode, y);
                this.GraphCanvas.Children.Add(refNode);

                // Line from root bottom center to reference top center
                var line = new Line
                {
                    X1 = rootCenterX,
                    Y1 = rootBottom,
                    X2 = x + nodeWidth / 2,
                    Y2 = y,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1.5
                };
                this.GraphCanvas.Children.Insert(0, line);
            }

            // Size canvas
            int totalRows = (int)Math.Ceiling((double)refCount / columns);
            this.GraphCanvas.Width = canvasWidth;
            this.GraphCanvas.Height = refsStartY + totalRows * (nodeHeight + vGap) + topMargin;
        }

        private void OpenReferenceDetail(ReferenceInfo refInfo)
        {
            // Look up the ObjectInfo by address across all captured types
            ObjectInfo matchedObject = null;
            foreach (var typeEntry in this.memoryStats.Types.Values)
            {
                matchedObject = typeEntry.Objects
                    .FirstOrDefault(o => o.Reference?.Address == refInfo.Address);
                if (matchedObject != null)
                {
                    break;
                }
            }

            if (matchedObject == null)
            {
                MessageBox.Show(
                    $"Object at 0x{refInfo.Address:X} ({refInfo.TypeName}) was not found in the captured snapshot.\n\nThis can happen when the object was filtered out during capture.",
                    "Object Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var syntheticType = new TypeInfo { TypeName = matchedObject.TypeName };
            syntheticType.AddObject(matchedObject);

            var detailWindow = new ObjectDetailWindow(syntheticType, this.memoryStats);
            detailWindow.Owner = this.Owner ?? this;
            detailWindow.Show();
        }

        private static Border CreateNodeVisual(string title, string detail, string borderColor, string bgColor, double width)
        {
            var border = new Border
            {
                Width = width,
                MinHeight = 50,
                Background = (Brush)new BrushConverter().ConvertFromString(bgColor)!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(borderColor)!,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Gray,
                    BlurRadius = 4,
                    ShadowDepth = 2,
                    Opacity = 0.3
                }
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = Brushes.Gray,
                FontSize = 10,
                FontFamily = new FontFamily("Consolas")
            });

            border.Child = stack;
            return border;
        }

        private class ObjectDisplayItem
        {
            public ObjectInfo ObjectInfo { get; }
            public string AddressText { get; }
            public string SizeText { get; }
            public string DisplayValueText { get; }
            public string ReferenceCountText { get; }

            public ObjectDisplayItem(ObjectInfo obj)
            {
                this.ObjectInfo = obj;
                this.AddressText = $"0x{obj.Reference?.Address:X}";
                this.SizeText = $"{obj.Size} bytes";
                this.DisplayValueText = obj.DisplayValue;
                this.ReferenceCountText = $"{obj.References.Count} refs";
            }
        }
    }
}
