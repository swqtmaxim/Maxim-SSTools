using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Serilog;

public sealed class BridgeHandler : IDisposable {
  private static readonly ILogger Logger = Log.ForContext<BridgeHandler>();

  private const int MaxMessageBytes = 8 * 1024 * 1024;

  private const int MaxIdLength = 128;

  private readonly CoreWebView2 m_webView;

  private readonly Window m_window;

  private readonly Dispatcher m_dispatcher;

  private readonly SemaphoreSlim m_scanGate = new(1, 1);

  private readonly object m_ctsGate = new();

  private CancellationTokenSource m_cancelSource = new();

  private bool m_disposed;

  public BridgeHandler(CoreWebView2 webView, Window window) {
    m_webView = webView;
    m_window = window;
    m_dispatcher = window.Dispatcher;
  }

  private void CancelActiveScan() {
    CancellationTokenSource previous;
    lock (m_ctsGate) {
      previous = m_cancelSource;
      m_cancelSource = new CancellationTokenSource();
    }
    try {
      previous.Cancel();
    } catch { }
    try {
      previous.Dispose();
    } catch { }
  }

  public void Dispose() {
    if (m_disposed)
      return;
    m_disposed = true;
    CancellationTokenSource? source = null;
    lock (m_ctsGate) {
      source = m_cancelSource;
    }
    try {
      source?.Cancel();
    } catch { }
    try {
      source?.Dispose();
    } catch { }
    try {
      m_scanGate.Dispose();
    } catch { }
  }

  public async Task HandleMessageAsync(string messageJson) {
    if (string.IsNullOrEmpty(messageJson) ||
        Encoding.UTF8.GetByteCount(messageJson) > MaxMessageBytes) {
      Logger.Warning("Rejected oversized or empty API message");
      return;
    }

    BridgeCall? message;
    try {
      message = JsonSerializer.Deserialize<BridgeCall>(messageJson, JsonOptions);
    } catch (JsonException ex) {
      Logger.Warning(ex, "Failed to deserialize API message");
      return;
    }

    if (message is null || string.IsNullOrEmpty(message.Id) || string.IsNullOrEmpty(message.Method))
      return;

    if (message.Id.Length > MaxIdLength) {
      Logger.Warning("Rejected API message with oversized id");
      return;
    }

    if (!AllowedMethods.Contains(message.Method)) {
      Logger.Warning("Unknown API method: {Method}", message.Method);
      await SendResponseAsync(message.Id, null, "Unknown method");
      return;
    }

    await HandleMethodAsync(message);
  }

  private async Task HandleMethodAsync(BridgeCall message) {
    object? finding = null;
    string? error = null;

    try {
      switch (message.Method) {
        case "minimize_window":
          await m_dispatcher.InvokeAsync(() => m_window.WindowState = WindowState.Minimized);
          break;
        case "close_window":
          await m_dispatcher.InvokeAsync(() => m_window.Close());
          break;
        case "service_checker_run":
          finding = await RunExclusiveAsync(ct => ServiceSweep.RunAsync(ct), 150);
          break;
        case "scheduler_run":
          finding = await RunExclusiveAsync<SchedulerReport>(ct => SchedulerSweep.RunAsync(ct), 150);
          break;
        case "leftover_run":
          finding = await RunExclusiveAsync<LeftoverReport>(ct => LeftoverSweep.RunAsync(ct), 150);
          break;
        case "usn_run":
          string drive = ParseStringArrayArg(message).FirstOrDefault() ?? "";
          finding = await RunExclusiveAsync<UsnReport>(ct => UsnSweep.RunAsync(drive, ct), 280);
          break;
        case "usn_drives":
          finding = await RunExclusiveAsync(() => UsnSweep.NtfsDrives());
          break;
        case "amcache_run":
          finding = await RunExclusiveAsync<AmcacheReport>(ct => AmcacheSweep.RunAsync(ct), 400);
          break;
        case "alt_detector_run":
          finding = await RunExclusiveAsync<AltReport>(ct => new AltSweep().RunAsync(ct: ct), 150);
          break;
        case "prefetch_run":
          finding = await RunExclusiveAsync<PrefetchReport>(ct => PrefetchSweep.RunAsync(ct), 280);
          break;
        case "prefetch_related_signatures":
          List<string> sigPaths = ParseStringArrayArg(message);
          finding = await RunExclusiveAsync<List<PrefetchRelatedSig>>(
              ct => Task.Run(() => PrefetchSweep.CheckRelatedSignatures(sigPaths, ct), ct), 120);
          break;
        case "bam_run":
          finding = await RunExclusiveAsync<BamReport>(ct => BamSweep.RunAsync(ct), 280);
          break;
        case "system_informer_status":
          finding = await Task.Run(() => (object?)SystemInformerDesk.GetStatus()).ConfigureAwait(false);
          break;
        case "system_informer_download_start":
          finding = await Task.Run(() => (object?)SystemInformerDesk.StartDownload()).ConfigureAwait(false);
          break;
        case "system_informer_progress":
          finding = await Task.Run(() => (object?)SystemInformerDesk.GetProgress()).ConfigureAwait(false);
          break;
        case "system_informer_cancel":
          await Task.Run(() => SystemInformerDesk.CancelDownload()).ConfigureAwait(false);
          finding = true;
          break;
        case "system_informer_check_update":
          finding = await SystemInformerDesk.CheckUpdateAsync().ConfigureAwait(false);
          break;
        case "system_informer_remove":
          finding = await Task.Run(() => (object?)SystemInformerDesk.RemoveInstall()).ConfigureAwait(false);
          break;
        case "system_informer_launch":
          finding = await Task.Run(() => (object?)SystemInformerDesk.LaunchExisting()).ConfigureAwait(false);
          break;
        case "system_informer_open_folder":
          finding = await Task.Run(() => (object?)SystemInformerDesk.OpenFolder()).ConfigureAwait(false);
          break;
        case "everything_status":
          finding = await Task.Run(() => (object?)EverythingDesk.GetStatus()).ConfigureAwait(false);
          break;
        case "everything_download_start":
          finding = await Task.Run(() => (object?)EverythingDesk.StartDownload()).ConfigureAwait(false);
          break;
        case "everything_progress":
          finding = await Task.Run(() => (object?)EverythingDesk.GetProgress()).ConfigureAwait(false);
          break;
        case "everything_cancel":
          await Task.Run(() => EverythingDesk.CancelDownload()).ConfigureAwait(false);
          finding = true;
          break;
        case "everything_launch":
          finding = await Task.Run(() => (object?)EverythingDesk.LaunchExisting()).ConfigureAwait(false);
          break;
        case "everything_open_folder":
          finding = await Task.Run(() => (object?)EverythingDesk.OpenFolder()).ConfigureAwait(false);
          break;
        case "everything_remove":
          finding = await Task.Run(() => (object?)EverythingDesk.RemoveInstall()).ConfigureAwait(false);
          break;
        case "config_get":
          finding = await Task.Run(() => (object?)ConfigStore.GetAll()).ConfigureAwait(false);
          break;
        case "config_set":
          string patch = message.Args.Length > 0 ? message.Args[0].GetRawText() : "{}";
          finding = await Task.Run(() => (object?)ConfigStore.SetPatch(patch)).ConfigureAwait(false);
          break;
        case "config_browse":
          finding = await m_dispatcher.InvokeAsync(() => (object?)ConfigStore.BrowseFolder());
          break;
        case "mc_profiles":
          finding = await Task.Run(() => (object?)McProfiles.GetAll()).ConfigureAwait(false);
          break;
        case "mc_profile_add":
          string mcAdd = message.Args.Length > 0 && message.Args[0].ValueKind == JsonValueKind.String
              ? message.Args[0].GetString() ?? "" : "";
          finding = await Task.Run(() => (object?)McProfiles.AddManual(mcAdd)).ConfigureAwait(false);
          break;
        case "mc_profile_remove":
          string mcDel = message.Args.Length > 0 && message.Args[0].ValueKind == JsonValueKind.String
              ? message.Args[0].GetString() ?? "" : "";
          finding = await Task.Run(() => (object?)McProfiles.RemoveManual(mcDel)).ConfigureAwait(false);
          break;
        case "report_save":
          string html = message.Args.Length > 0 && message.Args[0].ValueKind == JsonValueKind.String
              ? message.Args[0].GetString() ?? "" : "";
          string rname = message.Args.Length > 1 && message.Args[1].ValueKind == JsonValueKind.String
              ? message.Args[1].GetString() ?? "" : "";
          finding = await Task.Run(() => (object?)ConfigStore.SaveReport(html, rname)).ConfigureAwait(false);
          break;
        case "report_open":
          string rpath = message.Args.Length > 0 && message.Args[0].ValueKind == JsonValueKind.String
              ? message.Args[0].GetString() ?? "" : "";
          finding = await Task.Run(() => (object?)ConfigStore.OpenReport(rpath)).ConfigureAwait(false);
          break;
        case "report_read":
          string rdname = message.Args.Length > 0 && message.Args[0].ValueKind == JsonValueKind.String
              ? message.Args[0].GetString() ?? "" : "";
          finding = await Task.Run(() => (object?)ConfigStore.ReadCaseFile(rdname)).ConfigureAwait(false);
          break;
        case "hash_run":
          List<string> hashPaths = ParseStringArrayArg(message);
          finding = await RunExclusiveAsync<List<FileHashResult>>(
              ct => Task.Run(() => HashUtil.HashPaths(hashPaths, ct), ct), 280);
          break;
        case "shot_run":
          finding = await Task.Run(() => (object?)ShotDesk.Scan()).ConfigureAwait(false);
          break;
        case "cancel_scan":
          CancelActiveScan();
          finding = true;
          break;
        case "alt_detector_clear":
          finding = await RunExclusiveAsync(() => {
            new AltSweep().Clear();
            return true;
          });
          break;
        case "relaunch_as_admin":
          if (RelaunchAsAdmin())
            finding = true;
          else
            error = "Could not relaunch with administrator rights (UAC was cancelled or the executable was not found).";
          break;
      }
    } catch (OperationCanceledException ex) {
      error = string.IsNullOrEmpty(ex.Message) ? "Scan cancelled." : ex.Message;
    } catch (Exception ex) {
      Logger.Error(ex, "API method {Method} failed", message.Method);
      error = ex.Message;
    }

    await SendResponseAsync(message.Id, finding, error);
  }

  private static List<string> ParseStringArrayArg(BridgeCall message) {
    var bucket = new List<string>();
    try {
      if (message.Args is null || message.Args.Length == 0)
        return bucket;
      JsonElement first = message.Args[0];
      if (first.ValueKind == JsonValueKind.Object &&
          first.TryGetProperty("paths", out JsonElement paths))
        first = paths;
      if (first.ValueKind != JsonValueKind.Array)
        return bucket;
      foreach (JsonElement el in first.EnumerateArray()) {
        if (el.ValueKind != JsonValueKind.String)
          continue;
        string? s = el.GetString();
        if (!string.IsNullOrWhiteSpace(s))
          bucket.Add(s);
      }
    } catch {
    }
    return bucket;
  }

  private bool RelaunchAsAdmin() {
    string? exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs().FirstOrDefault();
    if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
      return false;
    try {
      var psi = new ProcessStartInfo(exe) {
        UseShellExecute = true,
        Verb = "runas",
        WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
      };
      // The elevated child skips the single-instance guard: the old
      // process is still exiting when it starts.
      psi.ArgumentList.Add("--skip-single-instance");
      if (Process.Start(psi) is not null) {
        m_dispatcher.InvokeAsync(() => m_window.Close());
        return true;
      }
      return false;
    } catch (Exception ex) {
      Logger.Information(ex, "UAC elevation was cancelled or failed");
      return false;
    }
  }

  private async Task<object?> RunExclusiveAsync(Func<object?> work) {
    await m_scanGate.WaitAsync().ConfigureAwait(false);
    try {
      return await Task.Run(work).ConfigureAwait(false);
    } finally { m_scanGate.Release(); }
  }

  private async Task SendResponseAsync(string id, object? finding, string? error) {
    try {
      string responseJson = JsonSerializer.Serialize(new BridgeReply(id, finding, error), JsonOptions);

      await m_dispatcher.InvokeAsync(() => {
        try {
          m_webView.PostWebMessageAsString(responseJson);
        } catch (Exception ex) {
          Logger.Warning(ex, "Failed to post response for call {Id}", id);
        }
      });
    } catch (Exception ex) {
      Logger.Error(ex, "Failed to serialize response for call {Id}", id);
    }
  }

  private static readonly JsonSerializerOptions JsonOptions = new() {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true
  };

  private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
  {
        "minimize_window", "close_window",
        "service_checker_run", "scheduler_run", "leftover_run",
        "usn_run", "usn_drives",
        "amcache_run",
        "alt_detector_run", "alt_detector_clear", "cancel_scan",
        "prefetch_run", "prefetch_related_signatures",
        "bam_run",
        "system_informer_status", "system_informer_download_start",
        "system_informer_progress", "system_informer_cancel",
        "system_informer_check_update", "system_informer_remove",
        "system_informer_launch", "system_informer_open_folder",
        "everything_status", "everything_download_start",
        "everything_progress", "everything_cancel",
        "everything_launch", "everything_open_folder", "everything_remove",
        "config_get", "config_set", "config_browse",
        "mc_profiles", "mc_profile_add", "mc_profile_remove",
        "report_save", "report_open", "report_read",
        "hash_run", "shot_run",
        "relaunch_as_admin"
    };

  private async Task<object?> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> work, int timeoutSeconds)
      where T : class? {
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    bool cancelled = false;
    CancellationTokenSource linked;
    CancellationTokenRegistration reg;
    lock (m_ctsGate) {
      linked = CancellationTokenSource.CreateLinkedTokenSource(m_cancelSource.Token, timeoutCts.Token);
      reg = m_cancelSource.Token.Register(() => cancelled = true);
    }
    using (linked)
    using (reg) {
      bool acquired = false;
      try {
        await m_scanGate.WaitAsync(linked.Token).ConfigureAwait(false);
        acquired = true;
        return await work(linked.Token).ConfigureAwait(false);
      } catch (OperationCanceledException) {
        throw new OperationCanceledException(cancelled ? "Scan cancelled." : "Scan timed out.");
      } finally {
        if (acquired)
          m_scanGate.Release();
      }
    }
  }
}
