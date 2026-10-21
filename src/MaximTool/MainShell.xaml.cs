using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Serilog;

public partial class MainShell : Window {
  private static readonly ILogger Logger = Log.ForContext<MainShell>();

  private BridgeHandler? m_bridge;

  private const int WM_GETMINMAXINFO = 0x0024;

  [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
  private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

  [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
  private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

  private async Task InjectCompatibilityShimAsync() {
    const string shim = """

            (function() {
                var pendingCalls = {};
                var callId = 0;

                window.chrome.webview.addEventListener('message', function(event) {
                    var raw = event.data;
                    var data;
                    if (typeof raw === 'string') {
                        try { data = JSON.parse(raw); } catch(e) { return; }
                    } else {
                        data = raw;
                    }
                    if (!data || !data.id) return;
                    var pending = pendingCalls[data.id];
                    if (pending) {
                        delete pendingCalls[data.id];
                        if (data.error) {
                            pending.reject(new Error(data.error));
                        } else {
                            pending.resolve(data.result);
                        }
                    }
                });

                window.pywebview = {
                    api: new Proxy({}, {
                        get: function(target, method) {
                            if (method === 'then' || method === 'toJSON' ||
                                typeof method === 'symbol' || method === 'constructor') {
                                return undefined;
                            }
                            return function() {
                                var args = Array.prototype.slice.call(arguments);
                                var id = (++callId).toString();
                                return new Promise(function(resolve, reject) {
                                    var timer = setTimeout(function() {
                                        if (pendingCalls[id]) {
                                            delete pendingCalls[id];
                                            reject(new Error('API call timed out'));
                                        }
                                    }, 300000);
                                    pendingCalls[id] = {
                                        resolve: function(v) { clearTimeout(timer); resolve(v); },
                                        reject: function(e) { clearTimeout(timer); reject(e); }
                                    };
                                    window.chrome.webview.postMessage(JSON.stringify({
                                        id: id,
                                        method: method,
                                        args: args
                                    }));
                                });
                            };
                        }
                    })
                };
            })();
            """;

    await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(shim);
  }

  private void ConfigureWebView() {
    var settings = WebView.CoreWebView2.Settings;
    settings.IsStatusBarEnabled = false;
    settings.AreDefaultContextMenusEnabled = false;
    settings.AreDevToolsEnabled = false;
    settings.IsZoomControlEnabled = false;
    settings.AreBrowserAcceleratorKeysEnabled = false;
    settings.IsGeneralAutofillEnabled = false;
    settings.IsPasswordAutosaveEnabled = false;
    settings.IsWebMessageEnabled = true;

    m_bridge = new BridgeHandler(WebView.CoreWebView2, this);
    WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
  }

  public MainShell() {
    InitializeComponent();
    Loaded += OnWindowLoaded;
    SourceInitialized += OnSourceInitialized;
  }

  private void NavigateToApp() {
    string assetsDir = Path.Combine(AssetVault.Root, "Assets");

    if (!File.Exists(Path.Combine(assetsDir, "index.html"))) {
      Logger.Fatal("index.html not found at {Path}", assetsDir);
      MessageBox.Show(
          $"ERROR: index.html not found at {assetsDir}",
          "Maxim SS Tools",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      Close();
      return;
    }

    WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
        "app.maxim.local",
        assetsDir,
        CoreWebView2HostResourceAccessKind.Allow);

    WebView.CoreWebView2.Navigate("https://app.maxim.local/index.html");
  }

  protected override void OnClosed(EventArgs e) {
    try {
      if (WebView.CoreWebView2 is not null) {
        WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        WebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
      }
    } catch {
    }
    try {
      m_bridge?.Dispose();
    } catch { }
    m_bridge = null;
    try {
      WebView.Dispose();
    } catch { }
    base.OnClosed(e);
  }

  private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
    string uri = e.Uri ?? "";
    if (uri.StartsWith("https://app.maxim.local/", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
      return;
    e.Cancel = true;
    Logger.Warning("Blocked navigation to {Uri}", uri);
  }

  private void OnSourceInitialized(object? sender, EventArgs e) {
    var handle = new WindowInteropHelper(this).Handle;
    HwndSource.FromHwnd(handle)?.AddHook(WndProc);
  }

  private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
    string source = e.Source ?? "";
    if (!source.StartsWith("https://app.maxim.local/", StringComparison.OrdinalIgnoreCase)) {
      Logger.Warning("Ignored web message from {Source}", source);
      return;
    }

    string? message = e.TryGetWebMessageAsString();
    if (string.IsNullOrEmpty(message) || m_bridge is null)
      return;

    var handler = m_bridge;
    _ = Task.Run(async () => {
      try {
        await handler.HandleMessageAsync(message);
      } catch (Exception ex) {
        Logger.Error(ex, "API handler failed");
      }
    });
  }

  private async void OnWindowLoaded(object sender, RoutedEventArgs e) {
    try {
      NativeLoader.EnsureWebView2Loader();
      string userDataFolder = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
          "Maxim-Screenshare-Tool", "WebView2");

      var env = await CoreWebView2Environment.CreateAsync(
          userDataFolder: userDataFolder);

      await WebView.EnsureCoreWebView2Async(env);

      ConfigureWebView();
      await InjectCompatibilityShimAsync();
      NavigateToApp();

      Logger.Information("WebView2 initialized successfully");
    } catch (Exception ex) {
      Logger.Fatal(ex, "Failed to initialize WebView2");
      MessageBox.Show(
          $"Failed to initialize WebView2.\n\n{ex.Message}\n\n" +
          "Please ensure Microsoft Edge WebView2 Runtime is installed.",
          "Maxim SS Tools",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      Close();
    }
  }

  private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
    if (msg == WM_GETMINMAXINFO) {
      var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
      IntPtr monitor = MonitorFromWindow(hwnd, 0x00000002);

      var monitorInfo = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
      if (GetMonitorInfo(monitor, ref monitorInfo)) {
        var workArea = monitorInfo.WorkArea;
        mmi.MaxPosition = new POINT { X = workArea.Left, Y = workArea.Top };
        mmi.MaxSize = new POINT {
          X = workArea.Right - workArea.Left,
          Y = workArea.Bottom - workArea.Top
        };
      }

      Marshal.StructureToPtr(mmi, lParam, false);
      handled = true;
    }
    return IntPtr.Zero;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct POINT {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MINMAXINFO {
    public POINT Reserved;
    public POINT MaxSize;
    public POINT MaxPosition;
    public POINT MinTrackSize;
    public POINT MaxTrackSize;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MONITORINFO {
    public int Size;
    public RECT Monitor;
    public RECT WorkArea;
    public uint Flags;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }
}
