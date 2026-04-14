namespace MemoryWatchDogApp
{
    using System.Diagnostics;
    using System.Reflection;
    using System.Windows;
    using System.Windows.Navigation;

    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            this.InitializeComponent();

            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
            var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;

            string arch = Environment.Is64BitProcess ? "x64" : "x86";
            this.Title = $"About .NET MemorySpy ({arch})";
            this.VersionText.Text = $"Version {version}  ({arch})";
            this.CopyrightText.Text = copyright ?? string.Empty;
            this.CompanyText.Text = company ?? string.Empty;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            if (e.Uri != null && !string.IsNullOrEmpty(e.Uri.AbsoluteUri))
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
        }
    }
}
