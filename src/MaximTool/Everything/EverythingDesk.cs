using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Serilog;

internal static class EverythingDesk {
  private static readonly ILogger Logger = Log.ForContext(typeof(EverythingDesk));

  private const string PortableUrl = "https://www.voidtools.com/Everything-1.4.1.1028.x64.zip";

  private const string KnownVersion = "1.4.1.1028";

  private const string AssetName = "Everything-1.4.1.1028.x64.zip";

  private static readonly object ProgressGate = new();

  private static EverythingProgress m_progress = new();

  private static CancellationTokenSource? m_downloadCts;

  private static Task? m_downloadTask;

  private static readonly HttpClient SharedHttp;

  public static string BaseFolder => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Maxim-Screenshare-Tool", "everything");

  public static void CancelDownload() {
    lock (ProgressGate) {
      try {
        m_downloadCts?.Cancel();
      } catch { }
    }
  }

  private static EverythingProgress CloneLocked() {
    return new EverythingProgress {
      Active = m_progress.Active,
      Done = m_progress.Done,
      Ok = m_progress.Ok,
      Stage = m_progress.Stage,
      Percent = m_progress.Percent,
      DownloadedBytes = m_progress.DownloadedBytes,
      TotalBytes = m_progress.TotalBytes,
      Version = m_progress.Version,
      ExePath = m_progress.ExePath,
      Error = m_progress.Error
    };
  }

  private static async Task DownloadCoreAsync(CancellationToken ct) {
    try {
      Directory.CreateDirectory(BaseFolder);
      string zipPath = Path.Combine(BaseFolder, "everything-portable-latest.zip");
      if (File.Exists(zipPath)) {
        try {
          File.Delete(zipPath);
        } catch { }
      }

      SetProgress("Downloading " + AssetName + "…", 5);
      await DownloadFileAsync(PortableUrl, zipPath, ct).ConfigureAwait(false);

      ct.ThrowIfCancellationRequested();
      SetProgress("Extracting portable package…", 88);
      string exe = await Task.Run(() => ExtractPortable(zipPath, BaseFolder, ct), ct).ConfigureAwait(false);

      ct.ThrowIfCancellationRequested();
      try {
        File.Delete(zipPath);
      } catch { }

      FinishProgress(true, null, exe, "Ready — " + KnownVersion + " extracted.");
    } catch (OperationCanceledException) {
      FinishProgress(false, "Download cancelled.", null, "Download cancelled.");
    } catch (Exception ex) {
      Logger.Error(ex, "Everything download failed");
      FinishProgress(false, FriendlyError(ex), null, "Download failed.");
    }
  }

  private static async Task DownloadFileAsync(string url, string destPath, CancellationToken ct) {
    using var resp = await SharedHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    long? total = resp.Content.Headers.ContentLength;
    lock (ProgressGate) {
      m_progress.TotalBytes = total ?? 0;
    }

    await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    await using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
    byte[] buffer = new byte[81920];
    long read = 0;
    int n;
    while ((n = await net.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) {
      await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
      read += n;
      int pct = total is > 0 ? (int)(5 + 80L * read / total.Value) : 5;
      SetProgress($"Downloading… {FormatBytes(read)}" + (total is > 0 ? $" / {FormatBytes(total.Value)}" : ""), pct, read, total ?? 0);
    }
    if (read == 0)
      throw new InvalidDataException("Downloaded file is empty.");
  }

  static EverythingDesk() {
    var handler = new SocketsHttpHandler {
      PooledConnectionLifetime = TimeSpan.FromMinutes(5),
      AutomaticDecompression = DecompressionMethods.All
    };
    SharedHttp = new HttpClient(handler, disposeHandler: true) {
      Timeout = Timeout.InfiniteTimeSpan
    };
    SharedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Maxim-Screenshare-Tool/1.0");
  }

  private static string ExtractPortable(string zipPath, string baseFolder, CancellationToken ct) {
    string extractDir = Path.Combine(baseFolder, "portable");
    if (Directory.Exists(extractDir)) {
      foreach (string entry in Directory.EnumerateFileSystemEntries(extractDir)) {
        ct.ThrowIfCancellationRequested();
        try {
          if (File.Exists(entry))
            File.Delete(entry);
          else
            Directory.Delete(entry, recursive: true);
        } catch { }
      }
    } else {
      Directory.CreateDirectory(extractDir);
    }

    try {
      ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
    } catch (Exception ex) {
      throw new InvalidDataException("ZIP extraction failed: " + ex.Message, ex);
    }

    string? exe = FindExe(baseFolder);
    if (exe is null)
      throw new FileNotFoundException("Everything.exe was not found inside the portable archive.");
    return exe;
  }

  private static string? FindExe(string folder) {
    try {
      if (!Directory.Exists(folder))
        return null;
      bool x64 = Environment.Is64BitOperatingSystem;
      string[] ordered =
      {
                Path.Combine(folder, "portable", x64 ? "Everything64.exe" : "Everything.exe"),
                Path.Combine(folder, "portable", "Everything.exe"),
                Path.Combine(folder, x64 ? "Everything64.exe" : "Everything.exe"),
                Path.Combine(folder, "Everything.exe"),
            };
      foreach (string p in ordered)
        if (File.Exists(p))
          return p;
      string[] hits = Directory.GetFiles(folder, "Everything*.exe", SearchOption.AllDirectories);
      Array.Sort(hits, StringComparer.OrdinalIgnoreCase);
      foreach (string h in hits) {
        string lower = h.ToLowerInvariant();
        if (lower.Contains("setup") || lower.Contains("installer"))
          continue;
        return h;
      }
      return null;
    } catch {
      return null;
    }
  }

  private static void FinishProgress(bool ok, string? error, string? exePath, string stage) {
    lock (ProgressGate) {
      m_progress.Active = false;
      m_progress.Done = true;
      m_progress.Ok = ok;
      m_progress.Error = error;
      m_progress.ExePath = exePath;
      m_progress.Stage = stage;
      m_progress.Percent = ok ? 100 : m_progress.Percent;
    }
  }

  private static string FormatBytes(long bytes) {
    const long KB = 1024;
    const long MB = 1024 * KB;
    if (bytes >= MB)
      return $"{bytes / (double)MB:0.0} MB";
    if (bytes >= KB)
      return $"{bytes / (double)KB:0.0} KB";
    return $"{bytes} B";
  }

  private static string FriendlyError(Exception ex) {
    string msg = ex.Message;
    if (ex is HttpRequestException || ex is TaskCanceledException)
      return "Network error while contacting voidtools.com. Check the connection and retry. (" + msg + ")";
    if (ex is UnauthorizedAccessException)
      return "Write blocked — check AV/folder rights, then retry. (" + msg + ")";
    if (ex is InvalidDataException || msg.Contains("zip", StringComparison.OrdinalIgnoreCase))
      return "Extraction failed — the archive looks corrupt. Retry. (" + msg + ")";
    return "Download failed: " + msg;
  }

  public static EverythingProgress GetProgress() {
    lock (ProgressGate) {
      return CloneLocked();
    }
  }

  public static EverythingStatus GetStatus() {
    string folder = BaseFolder;
    string? exe = FindExe(folder);
    var status = new EverythingStatus {
      Installed = exe is not null,
      ExePath = exe,
      Folder = folder,
      LatestKnown = KnownVersion
    };
    if (exe is null) {
      status.CheckNote = "Not installed — press Download & Launch.";
      return status;
    }
    status.Version = ReadVersion(exe);
    try {
      status.FileSizeLabel = FormatBytes(new FileInfo(exe).Length);
    } catch { }
    bool same = string.Equals(status.Version?.Trim(), KnownVersion, StringComparison.OrdinalIgnoreCase);
    status.UpdateAvailable = !same;
    status.CheckNote = same
        ? "Installed build matches the known portable (" + KnownVersion + ")."
        : "Installed (" + (status.Version ?? "?") + ") differs from known portable (" + KnownVersion + ").";
    return status;
  }

  public static string LaunchExisting() {
    string? exe = FindExe(BaseFolder);
    if (exe is null)
      throw new FileNotFoundException("No portable Everything found. Download it first.");
    var psi = new ProcessStartInfo(exe) {
      UseShellExecute = true,
      WorkingDirectory = Path.GetDirectoryName(exe) ?? BaseFolder
    };
    Process.Start(psi);
    return exe;
  }

  public static string OpenFolder() {
    string folder = BaseFolder;
    Directory.CreateDirectory(folder);
    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") {
      UseShellExecute = true
    });
    return folder;
  }

  private static string? ReadVersion(string exe) {
    try {
      var info = FileVersionInfo.GetVersionInfo(exe);
      string? v = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
      return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    } catch {
      return null;
    }
  }

  public static string RemoveInstall() {
    string folder = BaseFolder;
    int removed = 0;
    try {
      if (Directory.Exists(folder)) {
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder)) {
          try {
            if (File.Exists(entry))
              File.Delete(entry);
            else
              Directory.Delete(entry, recursive: true);
            removed++;
          } catch { }
        }
      }
    } catch (Exception ex) {
      Logger.Warning(ex, "Everything removal hit an error");
    }
    if (FindExe(folder) is not null)
      throw new IOException("Could not remove all files — close Everything first, then retry.");
    return $"Removed {removed} item{(removed == 1 ? "" : "s")} from {folder}.";
  }

  private static void SetProgress(string stage, int percent, long downloaded = -1, long total = -1) {
    lock (ProgressGate) {
      m_progress.Stage = stage;
      m_progress.Percent = Math.Clamp(percent, 0, 100);
      if (downloaded >= 0)
        m_progress.DownloadedBytes = downloaded;
      if (total >= 0)
        m_progress.TotalBytes = total;
    }
  }

  public static EverythingProgress StartDownload() {
    lock (ProgressGate) {
      if (m_progress.Active)
        return CloneLocked();
      try {
        m_downloadCts?.Cancel();
      } catch { }
      try {
        m_downloadCts?.Dispose();
      } catch { }
      m_downloadCts = new CancellationTokenSource();
      m_downloadCts.CancelAfter(TimeSpan.FromMinutes(10));
      m_progress = new EverythingProgress {
        Active = true,
        Done = false,
        Ok = false,
        Stage = "Downloading " + AssetName + "…",
        Percent = 0,
        Version = KnownVersion
      };
      CancellationToken ct = m_downloadCts.Token;
      m_downloadTask = Task.Run(() => DownloadCoreAsync(ct), ct);
      m_downloadTask.ContinueWith(
          t => Logger.Debug(t.Exception, "Everything download task faulted"),
          TaskContinuationOptions.OnlyOnFaulted);
      return CloneLocked();
    }
  }
}
