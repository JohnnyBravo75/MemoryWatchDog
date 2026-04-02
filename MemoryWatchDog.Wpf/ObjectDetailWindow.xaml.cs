namespace MemoryWatchDogApp
{
    using System;
    using System.Collections.ObjectModel;
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
        private TypeInfo? typeInfo;
        private MemoryStats? memoryStats;
        private Dictionary<ulong, ObjectInfo>? addressLookup;
        private List<ObjectDisplayItem>? allDisplayItems;
        private List<string>? systemNamespaces = MemoryStatsFilter.GetSystemNamespaces();
        private ObjectInfo? currentSelectedObject;

        public ObjectDetailWindow(TypeInfo typeInfo, MemoryStats memoryStats = null)
        {
            this.InitializeComponent();

            this.typeInfo = typeInfo;
            this.memoryStats = memoryStats;

            if (this.memoryStats != null)
            {
                this.addressLookup = new Dictionary<ulong, ObjectInfo>();
                foreach (var typeEntry in this.memoryStats.Types.Values)
                {
                    foreach (var obj in typeEntry.Objects)
                    {
                        var addr = obj.Reference?.Address ?? 0;
                        if (addr != 0 && !this.addressLookup.ContainsKey(addr))
                        {
                            this.addressLookup[addr] = obj;
                        }
                    }
                }
            }

            this.RetentionTreeView.AddHandler(
                TreeViewItem.ExpandedEvent,
                new RoutedEventHandler(this.RetentionTreeItem_Expanded));

            this.TypeNameHeader.Text = typeInfo.TypeName;
            this.ObjectCountText.Text = $"{typeInfo.Objects.Count} objects";
            this.Title = $"Object Details — {typeInfo.TypeName}";

            this.allDisplayItems = typeInfo.Objects
                .Select(o => new ObjectDisplayItem(o))
                .ToList();
            this.ApplyFilter();

            this.Closed += this.ObjectDetailWindow_Closed;
        }

        private void ObjectDetailWindow_Closed(object? sender, EventArgs e)
        {
            this.Closed -= this.ObjectDetailWindow_Closed;
            this.memoryStats = null;
            this.addressLookup = null;
            this.allDisplayItems = null;
            this.currentSelectedObject = null;
            this.typeInfo = null;
            this.Owner = null;
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
                this.currentSelectedObject = item.ObjectInfo;
                this.DrawDependencyGraph(item.ObjectInfo);
                this.UpdateRetentionGraph(item.ObjectInfo);
            }
            else
            {
                this.currentSelectedObject = null;
                this.GraphCanvas.Children.Clear();
                this.NoSelectionText.Text = "Select an object from the list to view its dependencies.";
                this.NoSelectionText.Visibility = Visibility.Visible;
                this.GraphScrollViewer.Visibility = Visibility.Collapsed;

                this.RetentionTreeView.ItemsSource = null;
                this.RetentionNoSelectionText.Text = "Select an object from the list to view the retention graph.";
                this.RetentionNoSelectionText.Visibility = Visibility.Visible;
                this.RetentionTreeView.Visibility = Visibility.Collapsed;
            }
        }

        private void ExcludeSystemTypesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (this.currentSelectedObject != null)
            {
                this.DrawDependencyGraph(this.currentSelectedObject);
                this.UpdateRetentionGraph(this.currentSelectedObject);
            }
        }

        private bool IsSystemType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            var typeNamespace = CommonUtil.GetNamespaceFromTypeName(typeName);
            foreach (var ns in this.systemNamespaces)
            {
                if (typeNamespace.StartsWith(ns, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawDependencyGraph(ObjectInfo obj)
        {
            this.GraphCanvas.Children.Clear();

            bool excludeSystem = this.ExcludeSystemTypesCheckBox.IsChecked == true;
            var references = excludeSystem
                ? obj.References.Where(r => !this.IsSystemType(r.TypeName)).ToList()
                : obj.References;

            if (references.Count == 0)
            {
                this.NoSelectionText.Text = obj.References.Count == 0
                    ? "This object has no references."
                    : "All references are system types (excluded by filter).";
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

            int refCount = references.Count;
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
                "#1565C0", "#E3F2FD", nodeWidth, obj.IsDisposed);
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
                var refInfo = references[i];
                int col = i % columns;
                int row = i / columns;

                double x = refsStartX + col * (nodeWidth + hGap);
                double y = refsStartY + row * (nodeHeight + vGap);

                var refNode = CreateNodeVisual(
                    refInfo.TypeName,
                    $"0x{refInfo.Address:X} | {refInfo.Size} bytes",
                    "#E65100", "#FFF3E0", nodeWidth, refInfo.IsDisposed);

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

        private void UpdateRetentionGraph(ObjectInfo obj)
        {
            if (this.addressLookup == null)
            {
                this.RetentionNoSelectionText.Text = "Retention graph requires snapshot data with memory stats.";
                this.RetentionNoSelectionText.Visibility = Visibility.Visible;
                this.RetentionTreeView.Visibility = Visibility.Collapsed;
                return;
            }

            bool excludeSystem = this.ExcludeSystemTypesCheckBox.IsChecked == true;
            var filteredRefs = excludeSystem
                ? obj.References.Where(r => !this.IsSystemType(r.TypeName)).ToList()
                : obj.References;

            if (filteredRefs.Count == 0)
            {
                this.RetentionNoSelectionText.Text = obj.References.Count == 0
                    ? "This object has no references."
                    : "All references are system types (excluded by filter).";
                this.RetentionNoSelectionText.Visibility = Visibility.Visible;
                this.RetentionTreeView.Visibility = Visibility.Collapsed;
                return;
            }

            this.RetentionNoSelectionText.Visibility = Visibility.Collapsed;
            this.RetentionTreeView.Visibility = Visibility.Visible;

            Func<string, bool> excludeFilter = excludeSystem ? this.IsSystemType : null;
            var rootNode = new RetentionNode(obj, this.addressLookup, new HashSet<ulong>(), excludeFilter);
            rootNode.LoadChildren();
            this.RetentionTreeView.ItemsSource = new[] { rootNode };
        }

        private void RetentionTreeItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem treeViewItem && treeViewItem.DataContext is RetentionNode node)
            {
                node.LoadChildren();
            }
        }

        private void OpenReferenceDetail(ReferenceInfo refInfo)
        {
            ObjectInfo matchedObject = null;
            this.addressLookup?.TryGetValue(refInfo.Address, out matchedObject);

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

        private static Border CreateNodeVisual(string title, string detail, string borderColor, string bgColor, double width, bool isDisposed = false)
        {
            var border = new Border
            {
                Width = width,
                MinHeight = 50,
                Background = (Brush)new BrushConverter().ConvertFromString(bgColor)!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(isDisposed ? "#D32F2F" : borderColor)!,
                BorderThickness = new Thickness(isDisposed ? 3 : 2),
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
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            if (isDisposed)
            {
                titlePanel.Children.Add(new TextBlock
                {
                    Text = " (disposed)",
                    Foreground = Brushes.Red,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            stack.Children.Add(titlePanel);
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
            public bool IsDisposed { get; }
            public string DisposedText { get; }

            public ObjectDisplayItem(ObjectInfo obj)
            {
                this.ObjectInfo = obj;
                this.AddressText = $"0x{obj.Reference?.Address:X}";
                this.SizeText = $"{obj.Size} bytes";
                this.DisplayValueText = obj.DisplayValue;
                this.ReferenceCountText = $"{obj.References.Count} refs";
                this.IsDisposed = obj.IsDisposed;
                this.DisposedText = obj.IsDisposed ? "(disposed)" : "";
            }
        }

        private class RetentionNode
        {
            private static readonly RetentionNode Placeholder = new RetentionNode("", "", "", false);

            private readonly List<ReferenceInfo> references;
            private readonly Dictionary<ulong, ObjectInfo> addressLookup;
            private readonly HashSet<ulong> ancestorAddresses;
            private readonly Func<string, bool> excludeFilter;
            private bool childrenLoaded;

            public string DisplayName { get; }
            public string DetailText { get; }
            public string ReferenceCountText { get; }
            public bool IsDisposed { get; }
            public string DisposedText { get; }
            public ObservableCollection<RetentionNode> Children { get; } = new ObservableCollection<RetentionNode>();

            private RetentionNode(string displayName, string detailText, string referenceCountText, bool isDisposed)
            {
                this.DisplayName = displayName;
                this.DetailText = detailText;
                this.ReferenceCountText = referenceCountText;
                this.IsDisposed = isDisposed;
                this.DisposedText = isDisposed ? "(disposed)" : "";
                this.childrenLoaded = true;
            }

            public RetentionNode(ObjectInfo obj, Dictionary<ulong, ObjectInfo> addressLookup, HashSet<ulong> ancestorAddresses, Func<string, bool> excludeFilter = null)
            {
                this.DisplayName = obj.TypeName;
                var addr = obj.Reference?.Address ?? 0;
                this.DetailText = $"0x{addr:X} | {obj.Size} bytes";
                this.references = obj.References;
                this.addressLookup = addressLookup;
                this.excludeFilter = excludeFilter;
                this.ReferenceCountText = obj.References.Count > 0 ? $"({obj.References.Count} refs)" : "";
                this.IsDisposed = obj.IsDisposed;
                this.DisposedText = obj.IsDisposed ? "(disposed)" : "";

                this.ancestorAddresses = new HashSet<ulong>(ancestorAddresses);
                if (addr != 0)
                {
                    this.ancestorAddresses.Add(addr);
                }

                if (obj.References.Count > 0)
                {
                    this.Children.Add(Placeholder);
                }
                else
                {
                    this.childrenLoaded = true;
                }
            }

            public void LoadChildren()
            {
                if (this.childrenLoaded)
                {
                    return;
                }

                this.childrenLoaded = true;
                this.Children.Clear();

                if (this.references == null)
                {
                    return;
                }

                foreach (var refInfo in this.references)
                {
                    if (this.excludeFilter != null && this.excludeFilter(refInfo.TypeName))
                    {
                        continue;
                    }

                    if (this.ancestorAddresses.Contains(refInfo.Address))
                    {
                        this.Children.Add(new RetentionNode(
                            $"\u21BB {refInfo.TypeName}",
                            $"0x{refInfo.Address:X} | {refInfo.Size} bytes",
                            "(cycle)",
                            refInfo.IsDisposed));
                    }
                    else if (this.addressLookup.TryGetValue(refInfo.Address, out var childObj))
                    {
                        this.Children.Add(new RetentionNode(childObj, this.addressLookup, this.ancestorAddresses, this.excludeFilter));
                    }
                    else
                    {
                        this.Children.Add(new RetentionNode(
                            refInfo.TypeName,
                            $"0x{refInfo.Address:X} | {refInfo.Size} bytes",
                            "",
                            refInfo.IsDisposed));
                    }
                }
            }
        }
    }
}
