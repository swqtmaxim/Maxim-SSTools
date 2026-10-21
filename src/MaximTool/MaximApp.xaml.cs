using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Serilog;

public partial class MaximApp : System.Windows.Application {
  private static bool AnotherInstanceRunning() {
    try {
      var me = System.Diagnostics.Process.GetCurrentProcess();
      foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName)) {
        if (p.Id != me.Id) {
          try {
            p.Dispose();
          } catch { }
          return true;
        }
        try {
          p.Dispose();
        } catch { }
      }
    } catch {
    }
    return false;
  }

  private static void ConfigureLogging() {
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.File(
            path: System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Maxim-Screenshare-Tool", "logs", "app-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
  }

  private void InstallExceptionHandlers() {
    DispatcherUnhandledException += (_, args) => {
      Log.Fatal(args.Exception, "Unhandled UI exception");
      args.Handled = true;
      MessageBox.Show(
          "An unexpected error occurred. Details were written to the log.",
          "Maxim SS Tools",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    };
    AppDomain.CurrentDomain.UnhandledException += (_, args) => {
      if (args.ExceptionObject is Exception ex)
        Log.Fatal(ex, "Unhandled domain exception");
    };
    TaskScheduler.UnobservedTaskException += (_, args) => {
      Log.Error(args.Exception, "Unobserved task exception");
      args.SetObserved();
    };
  }

  protected override void OnExit(ExitEventArgs e) {
    Log.CloseAndFlush();
    base.OnExit(e);
  }

  protected override void OnStartup(StartupEventArgs e) {
    bool skipGuard = e.Args.Any(a =>
        string.Equals(a, "--skip-single-instance", StringComparison.OrdinalIgnoreCase));
    if (!skipGuard && AnotherInstanceRunning()) {
      MessageBox.Show(
          "Maxim SS Tools is already running.",
          "Maxim SS Tools",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      Shutdown();
      return;
    }

    ConfigureLogging();
    InstallExceptionHandlers();

    base.OnStartup(e);
    var window = new MainShell();
    MainWindow = window;
    window.Show();
  }
}
