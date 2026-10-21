using System.IO;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;
using Serilog;
using System.Text.Json.Serialization;

public static class ServiceSweep {
  private static readonly ILogger Logger = Log.ForContext(typeof(ServiceSweep));

  private static readonly TimeSpan BootGrace = TimeSpan.FromMinutes(2);

  private const int RecycleItemCap = 15_000;

  private const int ScmEventScanLimit = 1_200;

  private static readonly uint[] MediaAccessModes = { NativeApi.GENERIC_READ, 0 };

  private static readonly string[] WatchedClearLabels =
  {
        "Security log (1102)",
        "System log (104)",
        "Application log (104)",
        "Setup log (104)"
    };

  private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };

  private const uint FILE_SHARE_DELETE = 0x00000004;

  private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

  private const uint StorageDeviceSeekPenaltyProperty = 7;

  private static void ApplyScmTimes(
      List<ServiceEntry> missing, CancellationToken ct) {
    try {
      var q = new EventLogQuery("System", PathType.LogName, "*[System[(EventID=7036)]]") {
        ReverseDirection = true
      };
      using var reader = new EventLogReader(q);

      int scanned = 0;
      int remaining = missing.Count;
      while (remaining > 0 && scanned++ < ScmEventScanLimit) {
        ct.ThrowIfCancellationRequested();
        using var evt = reader.ReadEvent();
        if (evt is null)
          break;
        if (!string.Equals(evt.ProviderName, "Service Control Manager", StringComparison.OrdinalIgnoreCase))
          continue;
        if (evt.Properties.Count < 2 || !evt.TimeCreated.HasValue)
          continue;

        string? svc = evt.Properties[0].Value?.ToString()?.Trim();
        string? extra = evt.Properties[1].Value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(svc))
          continue;

        if (IsStoppedState(extra))
          continue;

        foreach (var entry in missing) {
          if (!string.IsNullOrEmpty(entry.StartedAt))
            continue;
          if (!MatchesServiceName(svc, entry.Name, entry.Display))
            continue;
          entry.StartedAt = evt.TimeCreated.Value.ToString("yyyy-MM-dd HH:mm:ss");
          remaining--;
        }
      }
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      Logger.Debug(ex, "SCM event 7036 lookup failed");
    }
  }

  private static string ClassifyService(ServiceEntry entry, ServiceControllerStatus status, bool critical) {
    if (status == ServiceControllerStatus.Running)
      return "ok";
    if (status is ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending)
      return "warn";
    if (status == ServiceControllerStatus.Paused)
      return "warn";

    bool automatic = entry.StartType.StartsWith("Automatic", StringComparison.OrdinalIgnoreCase);
    bool disabled = string.Equals(entry.StartType, "Disabled", StringComparison.OrdinalIgnoreCase);

    if (critical && (disabled || automatic || status == ServiceControllerStatus.Stopped))
      return "bad";
    if (disabled)
      return "warn";
    return "warn";
  }

  private static List<AvProduct> CollectAvProducts() {
    var bucket = new List<AvProduct>();
    try {
      var scope = new System.Management.ManagementScope(@"\\.\root\SecurityCenter2");
      scope.Connect();
      using var searcher = new System.Management.ManagementObjectSearcher(scope,
          new System.Management.ObjectQuery("SELECT displayName, productState FROM AntiVirusProduct"));
      foreach (System.Management.ManagementObject mo in searcher.Get()) {
        try {
          string name = mo["displayName"]?.ToString()?.Trim() ?? "?";
          string state = mo["productState"]?.ToString() ?? "?";
          bucket.Add(new AvProduct { Name = name, State = "0x" + state, Source = "SecurityCenter" });
        } catch { } finally { try { mo.Dispose(); } catch { } }
        if (bucket.Count >= 20)
          break;
      }
    } catch { }
    try {
      var scope = new System.Management.ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
      scope.Connect();
      using var searcher = new System.Management.ManagementObjectSearcher(scope,
          new System.Management.ObjectQuery(
              "SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus"));
      foreach (System.Management.ManagementObject mo in searcher.Get()) {
        try {
          bool av = false, rtp = false;
          try {
            av = Convert.ToBoolean(mo["AntivirusEnabled"]);
          } catch { }
          try {
            rtp = Convert.ToBoolean(mo["RealTimeProtectionEnabled"]);
          } catch { }
          bucket.Add(new AvProduct {
            Name = "Windows Defender",
            State = (av ? "AV-ON" : "AV-OFF") + " " + (rtp ? "RTP-ON" : "RTP-OFF"),
            Source = "Defender"
          });
        } catch { } finally { try { mo.Dispose(); } catch { } }
        break;
      }
    } catch { }
    return bucket;
  }

  private static ServiceBootInfo CollectBoot(DateTime bootLocal) {
    var info = new ServiceBootInfo {
      LastBoot = bootLocal.ToString("yyyy-MM-dd HH:mm:ss")
    };

    try {
      TimeSpan uptime = DateTime.Now - bootLocal;
      if (uptime < TimeSpan.Zero)
        uptime = TimeSpan.Zero;
      info.Uptime = FormatUptime(uptime);

      DateTime tickBoot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
      double diffMin = Math.Abs((tickBoot - bootLocal).TotalMinutes);
      if (diffMin > 5) {
        info.TickMismatch = true;
        info.TickBootEstimate = tickBoot.ToString("yyyy-MM-dd HH:mm:ss");
        info.TickNote = "Differs from registry \u2014 sleep or clock change";
      }
    } catch (Exception ex) {
      Logger.Debug(ex, "Boot time collection failed");
      info.LastBoot = info.LastBoot.Length > 0 ? info.LastBoot : "Unavailable";
    }

    return info;
  }

  private static List<ServiceDriveInfo> CollectDrives(CancellationToken ct) {
    var bucket = new List<ServiceDriveInfo>();
    foreach (var drive in DriveInfo.GetDrives()) {
      ct.ThrowIfCancellationRequested();
      try {
        if (!drive.IsReady || drive.DriveType == DriveType.CDRom)
          continue;

        string root = drive.Name.TrimEnd('\\');
        string fs = string.IsNullOrEmpty(drive.DriveFormat) ? "Unknown" : drive.DriveFormat;
        string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "" : drive.VolumeLabel;
        string media = drive.DriveType == DriveType.Fixed ? GetMediaType(root) : "";
        string type = DescribeDriveType(drive.DriveType, media);

        bucket.Add(new ServiceDriveInfo {
          Letter = root,
          FileSystem = fs,
          Media = type,
          Label = label,
          Size = FormatDriveSize(drive)
        });
      } catch (Exception ex) {
        Logger.Debug(ex, "Skipped unreadable drive {Name}", drive.Name);
      }
    }
    return bucket;
  }

  private static ServiceEventsInfo CollectEvents(DateTime bootLocal, CancellationToken ct) {
    var info = new ServiceEventsInfo();
    DateTime timeThreshold = bootLocal + BootGrace;

    var clears = IntegrityDesk.DetectEventLogClears(bootLocal);
    info.Clears = WatchedClearLabels.Select(label => {
      var hit = clears.FirstOrDefault(c => string.Equals(c.Label, label, StringComparison.Ordinal));
      return new ServiceSignal {
        Key = label,
        Detected = hit is not null,
        When = hit?.When,
        Severity = hit is not null ? "bad" : "ok"
      };
    }).ToList();

    ct.ThrowIfCancellationRequested();
    info.Usn = CollectUsn(ct);

    ct.ThrowIfCancellationRequested();
    info.UsbDevices = CollectUsbDevices();
    info.AvProducts = CollectAvProducts();

    ct.ThrowIfCancellationRequested();
    info.UsnDeleted = SessionSignal("USN journal deleted (3079)", "Application", 3079, "Microsoft-Windows-Ntfs", bootLocal, "bad");
    info.LogClosed = SessionSignal("Security log closed (1100)", "Security", 1100, null, bootLocal, "warn");
    info.LogFull = SessionSignal("Security log full (1104)", "Security", 1104, null, bootLocal, "warn");
    info.ServiceInstalled = SessionSignal("Service installed (4697)", "Security", 4697, null, bootLocal, "bad");
    info.ProcCreated = LatestSignal("Process creation audit (4688)", "Security", 4688, null);
    info.UsbIn = LatestSignal("External device connected (6416)", "Security", 6416, null);
    info.UsbOut = LatestSignal("External device removed (6420)", "Security", 6420, null);

    DateTime? shutdown = IntegrityDesk.GetLatestEventTime("System", 1074, provider: null, after: null);
    info.LastShutdown = shutdown?.ToString("yyyy-MM-dd HH:mm:ss");

    DateTime? unexpected = IntegrityDesk.GetLatestEventTime("System", 6008, provider: null, after: bootLocal);
    info.UnexpectedShutdown = unexpected.HasValue;
    info.UnexpectedShutdownAt = unexpected?.ToString("yyyy-MM-dd HH:mm:ss");

    DateTime? timeChange = IntegrityDesk.GetLatestEventTime(
        "Security", 4616, "Microsoft-Windows-Security-Auditing", timeThreshold);
    info.ClockChanged = timeChange.HasValue;
    info.ClockChangedAt = timeChange?.ToString("yyyy-MM-dd HH:mm:ss");

    DateTime? logStart = IntegrityDesk.GetLatestEventTime("System", 6005, "EventLog", null)
                         ?? IntegrityDesk.GetLatestEventTime("System", 6005, "Microsoft-Windows-Eventlog", null);
    if (logStart.HasValue) {
      info.EventLogStartAt = logStart.Value.ToString("yyyy-MM-dd HH:mm:ss");
      if (logStart.Value >= timeThreshold) {
        info.EventLogRestarted = true;
        info.EventLogStartNote = "Restarted this session";
      } else {
        info.EventLogStartNote = "Boot";
      }
    }

    DateTime? device = IntegrityDesk.GetLatestEventTime("Microsoft-Windows-Kernel-PnP/Configuration", 400, null, bootLocal)
                       ?? IntegrityDesk.GetLatestEventTime("System", 400, null, bootLocal)
                       ?? IntegrityDesk.GetLatestEventTime("System", 225, null, bootLocal);
    info.LastDeviceChange = device?.ToString("yyyy-MM-dd HH:mm:ss");

    return info;
  }

  private static RecycleBinInfo CollectRecycleBin(DateTime bootLocal, CancellationToken ct) {
    var info = new RecycleBinInfo();
    var newest = default((DateTime When, string Path)?);
    var oldest = default((DateTime When, string Path)?);

    foreach (var drive in DriveInfo.GetDrives()) {
      ct.ThrowIfCancellationRequested();
      try {
        if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network)
          continue;

        string recycleBinPath = Path.Combine(drive.Name, "$Recycle.Bin");
        if (!Directory.Exists(recycleBinPath))
          continue;

        info.Volumes++;
        ScanRecycleBin(recycleBinPath, bootLocal, info, ref newest, ref oldest, ct);
      } catch {
        info.Inaccessible++;
      }
    }

    if (newest.HasValue) {
      info.NewestAt = newest.Value.When.ToString("yyyy-MM-dd HH:mm:ss");
      info.NewestPath = newest.Value.Path;
    }
    if (oldest.HasValue) {
      info.OldestAt = oldest.Value.When.ToString("yyyy-MM-dd HH:mm:ss");
      info.OldestPath = oldest.Value.Path;
    }

    info.TotalSizeLabel = FormatBytes(info.TotalSize);
    return info;
  }

  private static List<ServiceEntry> CollectServices(CancellationToken ct) {
    var entries = new List<ServiceEntry>(WatchedServices.Length);

    foreach (var (names, fallbackDisplay, critical) in WatchedServices) {
      ct.ThrowIfCancellationRequested();
      var entry = new ServiceEntry {
        Name = names[0],
        Display = fallbackDisplay,
        Critical = critical
      };

      if (!TryOpenService(names, out ServiceController? service, out string resolvedName, out bool denied) ||
          service is null) {
        if (!names[0].EndsWith("*", StringComparison.Ordinal) ||
            !TryOpenPrefixedService(names[0].TrimEnd('*'), out service, out resolvedName, out denied) ||
            service is null) {
          entry.Status = denied ? "Denied" : "Missing";
          entry.Severity = critical ? "bad" : "warn";
          entries.Add(entry);
          continue;
        }
      }

      using (service) {
        entry.Name = resolvedName;
        try {
          entry.Display = service.DisplayName;
        } catch { entry.Display = fallbackDisplay; }

        try {
          entry.StartType = DescribeStartType(service, resolvedName);
        } catch { entry.StartType = "Unknown"; }

        ServiceControllerStatus status;
        try {
          status = service.Status;
        } catch {
          entry.Status = "Denied";
          entry.Severity = "bad";
          entries.Add(entry);
          continue;
        }

        entry.Status = DescribeStatus(status);
        if (status == ServiceControllerStatus.Running) {
          int pid = ForensicKit.GetServicePid(resolvedName, out bool shared);
          if (pid <= 0 && !string.Equals(resolvedName, names[0], StringComparison.OrdinalIgnoreCase))
            pid = ForensicKit.GetServicePid(names[0], out shared);
          entry.Pid = pid > 0 ? pid : null;
          entry.SharedHost = shared;
          if (pid > 0) {
            try {
              using var proc = Process.GetProcessById(pid);
              entry.StartedAt = proc.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            } catch {
            }
          }
        }

        entry.Severity = ClassifyService(entry, status, critical);
      }

      entries.Add(entry);
    }

    FillMissingStartTimes(entries, ct);
    return entries;
  }

  private static List<UsbDevice> CollectUsbDevices() {
    var bucket = new List<UsbDevice>();
    try {
      using var stor = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
          @"SYSTEM\CurrentControlSet\Enum\USBSTOR");
      if (stor is null)
        return bucket;
      foreach (string dev in stor.GetSubKeyNames()) {
        try {
          using var devKey = stor.OpenSubKey(dev);
          if (devKey is null)
            continue;
          foreach (string inst in devKey.GetSubKeyNames()) {
            if (bucket.Count >= 100)
              break;
            string name = dev;
            try {
              name = (devKey.OpenSubKey(inst)?.GetValue("FriendlyName") as string)?.Trim() ?? dev;
            } catch { }
            if (string.IsNullOrWhiteSpace(name))
              name = dev;
            bucket.Add(new UsbDevice { Name = name, Serial = inst });
          }
        } catch { }
        if (bucket.Count >= 100)
          break;
      }
    } catch { }
    return bucket;
  }

  private static List<ServiceUsnInfo> CollectUsn(CancellationToken ct) {
    var bucket = new List<ServiceUsnInfo>();
    var reader = new UsnReader();
    bool anyNtfs = false;

    foreach (var drive in DriveInfo.GetDrives()) {
      ct.ThrowIfCancellationRequested();
      try {
        if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network)
          continue;
        if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
          continue;

        anyNtfs = true;
        char letter = char.ToUpperInvariant(drive.Name[0]);
        var status = reader.CheckIntegrity(letter, BootGrace);
        var entry = new ServiceUsnInfo { Volume = letter.ToString() };

        if (status.JournalDisabled) {
          entry.State = "Disabled this session";
          entry.Severity = "bad";
        } else if (!status.JournalEnabled) {
          entry.State = status.Error ?? "Inaccessible";
          entry.Severity = "warn";
        } else if (status.JournalRecreated &&
                   DateTime.TryParseExact(status.JournalCreationTime,
                       "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out var created)) {
          entry.State = "Recreated this session";
          entry.When = created.ToString("yyyy-MM-dd HH:mm:ss");
          entry.Severity = "bad";
        } else {
          entry.State = "Active";
          entry.Severity = "ok";
        }

        bucket.Add(entry);
      } catch (Exception ex) {
        Logger.Debug(ex, "USN check failed for {Drive}", drive.Name);
      }
    }

    if (!anyNtfs) {
      bucket.Add(new ServiceUsnInfo {
        Volume = "",
        State = "No NTFS volumes available",
        Severity = "warn"
      });
    }

    return bucket;
  }

  private static int CountFindings(ServiceReport finding) {
    int n = 0;
    if (finding.Boot.TickMismatch)
      n++;
    n += finding.Services.Count(s => s.Severity == "bad");
    if (finding.Events is { Skipped: false } ev) {
      n += ev.Clears.Count(c => c.Detected);
      n += ev.Usn.Count(u => u.Severity == "bad");
      if (ev.UnexpectedShutdown)
        n++;
      if (ev.ClockChanged)
        n++;
      if (ev.EventLogRestarted)
        n++;
      foreach (var s in new[] { ev.UsnDeleted, ev.LogClosed, ev.LogFull, ev.ServiceInstalled })
        if (s is { Detected: true, Severity: "bad" })
          n++;
    }
    return n;
  }

  private static string DescribeDriveType(DriveType type, string media) {
    return type switch {
      DriveType.Fixed => media == "Unknown" ? "Fixed" : media,
      DriveType.Removable => "Removable",
      DriveType.Network => "Network",
      DriveType.Ram => "RAM Disk",
      _ => "Unknown"
    };
  }

  private static string DescribeStartType(ServiceController service, string serviceName) {
    ServiceStartMode mode;
    try {
      mode = service.StartType;
    } catch { return "Unknown"; }

    if (mode == ServiceStartMode.Automatic && IsDelayedAutoStart(serviceName))
      return "Automatic (Delayed)";

    return mode switch {
      ServiceStartMode.Automatic => "Automatic",
      ServiceStartMode.Manual => "Manual",
      ServiceStartMode.Disabled => "Disabled",
      ServiceStartMode.Boot => "Boot",
      ServiceStartMode.System => "System",
      _ => mode.ToString()
    };
  }

  private static string DescribeStatus(ServiceControllerStatus status) => status switch {
    ServiceControllerStatus.Running => "Running",
    ServiceControllerStatus.Stopped => "Stopped",
    ServiceControllerStatus.Paused => "Paused",
    ServiceControllerStatus.StartPending => "Start pending",
    ServiceControllerStatus.StopPending => "Stop pending",
    ServiceControllerStatus.ContinuePending => "Continue pending",
    ServiceControllerStatus.PausePending => "Pause pending",
    _ => status.ToString()
  };

  private static void FillMissingStartTimes(List<ServiceEntry> entries, CancellationToken ct) {
    var missing = entries
        .Where(e => string.Equals(e.Status, "Running", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(e.StartedAt))
        .ToList();
    if (missing.Count == 0)
      return;

    ApplyScmTimes(missing, ct);
  }

  private static string FormatBytes(long bytes) {
    double value = bytes;
    int i = 0;
    while (value >= 1024 && i < ByteUnits.Length - 1) {
      value /= 1024;
      i++;
    }
    string num = i == 0 ? value.ToString("0") : value.ToString("0.0");
    return num + " " + ByteUnits[i];
  }

  private static string FormatDriveSize(DriveInfo drive) {
    try {
      long total = drive.TotalSize;
      if (total <= 0)
        return "";
      long free = drive.TotalFreeSpace;
      return FormatBytes(free) + " free of " + FormatBytes(total);
    } catch {
      return "";
    }
  }

  private static string FormatUptime(TimeSpan up)
      => $"{(int)up.TotalHours}h {up.Minutes}m {up.Seconds}s";

  private static string GetMediaType(string root) {
    foreach (uint access in MediaAccessModes) {
      IntPtr h = NativeApi.CreateFileW(
          @"\\.\" + root,
          access,
          NativeApi.FILE_SHARE_READ | NativeApi.FILE_SHARE_WRITE | FILE_SHARE_DELETE,
          IntPtr.Zero, NativeApi.OPEN_EXISTING, 0, IntPtr.Zero);

      if (h == NativeApi.InvalidHandleValue)
        continue;

      try {
        byte[] query = new byte[12];
        BitConverter.GetBytes(StorageDeviceSeekPenaltyProperty).CopyTo(query, 0);
        BitConverter.GetBytes((uint)0).CopyTo(query, 4);

        byte[] output = new byte[12];
        if (NativeApi.DeviceIoControl(
            h, IOCTL_STORAGE_QUERY_PROPERTY, query, (uint)query.Length,
            output, (uint)output.Length, out _, IntPtr.Zero)) {
          bool incurs = output[8] != 0;
          return incurs ? "HDD" : "SSD";
        }
      } catch (Exception ex) {
        Logger.Debug(ex, "Media type query failed for {Root}", root);
      } finally {
        NativeApi.CloseHandle(h);
      }
    }

    return "Unknown";
  }

  private static bool IsDelayedAutoStart(string serviceName) {
    try {
      using var key = Registry.LocalMachine.OpenSubKey(
          @"SYSTEM\CurrentControlSet\Services\" + serviceName);
      object? value = key?.GetValue("DelayedAutostart");
      return value is int i && i != 0;
    } catch {
      return false;
    }
  }

  private static bool IsStoppedState(string? state) {
    if (string.IsNullOrEmpty(state))
      return false;
    return state.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0
        || state.IndexOf("paus", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static ServiceSignal LatestSignal(string key, string logName, int eventId, string? provider) {
    DateTime? when = IntegrityDesk.GetLatestEventTime(logName, eventId, provider, null);
    return new ServiceSignal {
      Key = key,
      Detected = when.HasValue,
      When = when?.ToString("yyyy-MM-dd HH:mm:ss"),
      Severity = "ok"
    };
  }

  private static bool MatchesServiceName(string? value, string serviceName, string displayName) {
    if (string.IsNullOrEmpty(value))
      return false;
    if (string.Equals(value, serviceName, StringComparison.OrdinalIgnoreCase))
      return true;
    if (string.Equals(serviceName, "SysMain", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(value, "Superfetch", StringComparison.OrdinalIgnoreCase))
      return true;
    if (string.Equals(serviceName, "Superfetch", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(value, "SysMain", StringComparison.OrdinalIgnoreCase))
      return true;
    return !string.IsNullOrEmpty(displayName) &&
           string.Equals(value, displayName, StringComparison.OrdinalIgnoreCase);
  }

  private static RecycleItem? ParseRecycleBinMetadata(string metaPath) {
    try {
      byte[] data;
      using (var fs = new FileStream(metaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
        if (fs.Length < 24 || fs.Length > 65_536)
          return null;
        data = new byte[(int)fs.Length];
        int read = 0;
        while (read < data.Length) {
          int n = fs.Read(data, read, data.Length - read);
          if (n == 0)
            return null;
          read += n;
        }
      }

      long size = BitConverter.ToInt64(data, 8);
      long fileTime = BitConverter.ToInt64(data, 16);
      if (fileTime <= 0)
        return null;

      DateTime deleted;
      try {
        deleted = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
      } catch { return null; }

      string path = ReadRecycleBinPath(data);
      if (string.IsNullOrEmpty(path))
        path = Path.GetFileNameWithoutExtension(metaPath);

      return new RecycleItem(path, size < 0 ? 0 : size, deleted);
    } catch {
      return null;
    }
  }

  private static string ReadRecycleBinPath(byte[] data) {
    if (data.Length < 24)
      return "";

    long version = BitConverter.ToInt64(data, 0);
    bool win10 = version == 2 || (version != 1 && data.Length >= 28);
    if (win10) {
      int pathLen = BitConverter.ToInt32(data, 24);
      int maxChars = (data.Length - 28) / 2;
      if (pathLen > 0 && pathLen <= maxChars)
        return Encoding.Unicode.GetString(data, 28, pathLen * 2).TrimEnd('\0');
    }

    int start = (version == 2 && data.Length >= 28) ? 28 : 24;
    var buff = new StringBuilder();
    for (int i = start; i + 1 < data.Length; i += 2) {
      char c = (char)((data[i + 1] << 8) | data[i]);
      if (c == '\0')
        break;
      if (c < 0x20 && c != '\t')
        break;
      buff.Append(c);
    }
    return buff.ToString();
  }

  public static Task<ServiceReport> RunAsync(CancellationToken ct = default)
      => RunCoreAsync(ct);

  private static async Task<ServiceReport> RunCoreAsync(CancellationToken ct) {
    var finding = new ServiceReport {
      Admin = ForensicKit.IsAdministrator()
    };
    DateTime bootLocal = ForensicKit.GetBootTimeLocal();
    finding.Boot = CollectBoot(bootLocal);

    Task<List<ServiceDriveInfo>> drivesTask = Task.Run(() => CollectDrives(ct), ct);
    Task<List<ServiceEntry>> servicesTask = Task.Run(() => CollectServices(ct), ct);
    await Task.WhenAll(drivesTask, servicesTask).ConfigureAwait(false);
    finding.Drives = drivesTask.Result;
    finding.Services = servicesTask.Result;

    if (finding.Admin) {
      Task<ServiceEventsInfo> eventsTask = Task.Run(() => CollectEvents(bootLocal, ct), ct);
      Task<RecycleBinInfo> recycleTask = Task.Run(() => CollectRecycleBin(bootLocal, ct), ct);
      await Task.WhenAll(eventsTask, recycleTask).ConfigureAwait(false);
      finding.Events = eventsTask.Result;
      finding.Recycle = recycleTask.Result;
    } else {
      finding.Events = new ServiceEventsInfo { Skipped = true };
      finding.Recycle = new RecycleBinInfo { Skipped = true };
    }

    finding.FindingCount = CountFindings(finding);
    try {
      var (detected, vendor) = CollectVm();
      finding.VmDetected = detected;
      finding.VmVendor = vendor;
    } catch { }
    return finding;
  }

  private static void ScanRecycleBin(
      string recycleBinPath,
      DateTime bootLocal,
      RecycleBinInfo info,
      ref (DateTime When, string Path)? newest,
      ref (DateTime When, string Path)? oldest,
      CancellationToken ct) {
    foreach (var sidDir in new DirectoryInfo(recycleBinPath)
        .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)) {
      ct.ThrowIfCancellationRequested();
      try {
        foreach (var meta in sidDir.EnumerateFiles("$I*", SearchOption.TopDirectoryOnly)) {
          if (info.Items >= RecycleItemCap)
            return;

          var entry = ParseRecycleBinMetadata(meta.FullName);
          if (entry is null) {
            info.Inaccessible++;
            continue;
          }

          info.Items++;
          info.TotalSize += entry.Size;
          if (entry.DeletedAt >= bootLocal)
            info.DeletedThisSession++;

          if (!oldest.HasValue || entry.DeletedAt < oldest.Value.When)
            oldest = (entry.DeletedAt, entry.OriginalPath);
          if (!newest.HasValue || entry.DeletedAt > newest.Value.When)
            newest = (entry.DeletedAt, entry.OriginalPath);
        }
      } catch {
        info.Inaccessible++;
      }
    }
  }

  private static ServiceSignal SessionSignal(
      string key, string logName, int eventId, string? provider, DateTime bootLocal, string severity) {
    DateTime? when = IntegrityDesk.GetLatestEventTime(logName, eventId, provider, bootLocal);
    return new ServiceSignal {
      Key = key,
      Detected = when.HasValue,
      When = when?.ToString("yyyy-MM-dd HH:mm:ss"),
      Severity = when.HasValue ? severity : "ok"
    };
  }

  private static bool TryOpenPrefixedService(string prefix, out ServiceController? service, out string resolvedName, out bool denied) {
    denied = false;
    service = null;
    resolvedName = prefix;
    ServiceController[] all;
    try {
      all = ServiceController.GetServices();
    } catch { return false; }
    foreach (var sc in all) {
      bool match;
      try {
        match = sc.ServiceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
      } catch { sc.Dispose(); continue; }
      if (!match) {
        sc.Dispose();
        continue;
      }
      try {
        _ = sc.Status;
        service = sc;
        resolvedName = sc.ServiceName;
        return true;
      } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException) {
        sc.Dispose();
        denied = true;
      } catch {
        sc.Dispose();
      }
    }
    return false;
  }

  private static bool TryOpenService(string[] names, out ServiceController? service, out string resolvedName, out bool denied) {
    denied = false;
    foreach (string name in names) {
      ServiceController? sc = null;
      try {
        sc = new ServiceController(name);
        _ = sc.Status;
        service = sc;
        resolvedName = name;
        return true;
      } catch (InvalidOperationException) {
        sc?.Dispose();
      } catch (OperationCanceledException) { sc?.Dispose(); throw; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException) {
        sc?.Dispose();
        denied = true;
        Logger.Debug(ex, "Access denied opening service {Name}", name);
      } catch (Exception ex) {
        sc?.Dispose();
        Logger.Debug(ex, "Could not open service {Name}", name);
      }
    }

    service = null;
    resolvedName = names[0];
    return false;
  }

  private static readonly (string[] Names, string FallbackDisplay, bool Critical)[] WatchedServices =
  {
        (new[] { "SysMain", "Superfetch" }, "SysMain / Superfetch", true),
        (new[] { "PcaSvc" }, "Program Compatibility Assistant Service", true),
        (new[] { "DPS" }, "Diagnostic Policy Service", true),
        (new[] { "EventLog" }, "Windows Event Log", true),
        (new[] { "Schedule" }, "Task Scheduler", true),
        (new[] { "Bam" }, "Background Activity Moderator", true),
        (new[] { "DusmSvc" }, "Data Usage", false),
        (new[] { "Appinfo" }, "Application Information", true),
        (new[] { "CDPSvc" }, "Connected Devices Platform Service", false),
        (new[] { "DcomLaunch" }, "DCOM Server Process Launcher", true),
        (new[] { "CDPUserSvc*" }, "Activities Cache", true),
        (new[] { "PlugPlay" }, "Plug and Play", true),
        (new[] { "WSearch" }, "Windows Search", false)
    };

  private static (bool Detected, string Vendor) CollectVm() {
    string maker = "", model = "";
    bool hyper = false;
    try {
      var scope = new System.Management.ManagementScope(@"\\.\root\cimv2");
      scope.Connect();
      using var searcher = new System.Management.ManagementObjectSearcher(scope,
          new System.Management.ObjectQuery(
              "SELECT Manufacturer, Model, HypervisorPresent FROM Win32_ComputerSystem"));
      foreach (System.Management.ManagementObject mo in searcher.Get()) {
        try {
          maker = (mo["Manufacturer"]?.ToString() ?? "").Trim();
          model = (mo["Model"]?.ToString() ?? "").Trim();
          try {
            hyper = Convert.ToBoolean(mo["HypervisorPresent"]);
          } catch { }
        } catch { } finally { try { mo.Dispose(); } catch { } }
        break;
      }
    } catch { }
    string hay = (maker + " " + model).ToLowerInvariant();
    string[] marks = { "vmware", "virtualbox", "innotek", "qemu", "kvm", "xen", "hyper-v",
            "parallels", "bhyve", "bochs", "microsoft corporation" };
    // "Microsoft Corporation" alone also matches Hyper-V guests; pair it
    // with a hypervisor flag or known virtual model before calling it.
    bool vm = hyper;
    string vendor = (maker + " " + model).Trim();
    foreach (string m in marks) {
      if (m == "microsoft corporation")
        continue;
      if (hay.Contains(m)) {
        vm = true;
        vendor = maker.Trim();
        break;
      }
    }
    if (!vm && hay.Contains("microsoft corporation") &&
        (hyper || hay.Contains("virtual")))
      vm = true;
    return (vm, vendor);
  }

  private sealed record RecycleItem(string OriginalPath, long Size, DateTime DeletedAt);
}
