namespace MemoryWatchDogApp
{
    using System.Windows;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// When set, the app was launched with -attach &lt;pid&gt; and should
        /// automatically attach to this process on startup.
        /// </summary>
        public static int? AutoAttachProcessId { get; private set; }

        public App()
        {
            ParseCommandLineArgs();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                OnUnhandledException((Exception)e.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");
            };

            DispatcherUnhandledException += (s, e) =>
            {
                OnUnhandledException(e.Exception, "Application.Current.DispatcherUnhandledException");
                e.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                OnUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
                e.SetObserved();
            };
        }

        private static void ParseCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-attach", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(args[i + 1], out int pid))
                {
                    AutoAttachProcessId = pid;
                    break;
                }
            }
        }

        private void OnUnhandledException(Exception exception, string source)
        {
            string message = $"Unhandled exception ({source})";
            try
            {
                var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName();
                message = string.Format("Unhandled exception in {0} v{1}", assemblyName.Name, assemblyName.Version);
            }
            catch (Exception ex)
            {
                // _logger.Error(ex, "Exception in LogUnhandledException");
            }
            finally
            {
                // _logger.Error(exception, message);

                Console.WriteLine(exception.ToString());
            }
        }
    }
}
