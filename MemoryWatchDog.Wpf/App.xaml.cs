namespace MemoryWatchDogApp
{
    using System.Windows;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
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
