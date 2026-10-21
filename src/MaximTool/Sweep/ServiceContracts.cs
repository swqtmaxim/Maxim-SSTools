using System.Text.Json.Serialization;

public sealed class ServiceReport {
  [JsonPropertyName("admin")]
  public bool Admin {
    get; set;
  }

  [JsonPropertyName("error")]
  public string? Error {
    get; set;
  }

  [JsonPropertyName("findingCount")]
  public int FindingCount {
    get; set;
  }

  [JsonPropertyName("boot")]
  public ServiceBootInfo Boot { get; set; } = new();

  [JsonPropertyName("drives")]
  public List<ServiceDriveInfo> Drives { get; set; } = new();

  [JsonPropertyName("services")]
  public List<ServiceEntry> Services { get; set; } = new();

  [JsonPropertyName("events")]
  public ServiceEventsInfo? Events {
    get; set;
  }

  [JsonPropertyName("recycle")]
  public RecycleBinInfo? Recycle {
    get; set;
  }

  [JsonPropertyName("vmDetected")]
  public bool VmDetected {
    get; set;
  }

  [JsonPropertyName("vmVendor")]
  public string VmVendor { get; set; } = "";
}
public sealed class ServiceBootInfo {
  [JsonPropertyName("lastBoot")]
  public string LastBoot { get; set; } = "";

  [JsonPropertyName("uptime")]
  public string Uptime { get; set; } = "";

  [JsonPropertyName("tickMismatch")]
  public bool TickMismatch {
    get; set;
  }

  [JsonPropertyName("tickBootEstimate")]
  public string? TickBootEstimate {
    get; set;
  }

  [JsonPropertyName("tickNote")]
  public string? TickNote {
    get; set;
  }
}
public sealed class ServiceDriveInfo {
  [JsonPropertyName("letter")]
  public string Letter { get; set; } = "";

  [JsonPropertyName("fileSystem")]
  public string FileSystem { get; set; } = "";

  [JsonPropertyName("media")]
  public string Media { get; set; } = "";

  [JsonPropertyName("label")]
  public string Label { get; set; } = "";

  [JsonPropertyName("size")]
  public string Size { get; set; } = "";
}
public sealed class ServiceEntry {
  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("display")]
  public string Display { get; set; } = "";

  [JsonPropertyName("status")]
  public string Status { get; set; } = "";

  [JsonPropertyName("startType")]
  public string StartType { get; set; } = "";

  [JsonPropertyName("startedAt")]
  public string? StartedAt {
    get; set;
  }

  [JsonPropertyName("pid")]
  public int? Pid {
    get; set;
  }

  [JsonPropertyName("sharedHost")]
  public bool SharedHost {
    get; set;
  }

  [JsonPropertyName("critical")]
  public bool Critical {
    get; set;
  }

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}
public sealed class ServiceEventsInfo {
  [JsonPropertyName("skipped")]
  public bool Skipped {
    get; set;
  }

  [JsonPropertyName("clears")]
  public List<ServiceSignal> Clears { get; set; } = new();

  [JsonPropertyName("usn")]
  public List<ServiceUsnInfo> Usn { get; set; } = new();

  [JsonPropertyName("lastShutdown")]
  public string? LastShutdown {
    get; set;
  }

  [JsonPropertyName("unexpectedShutdown")]
  public bool UnexpectedShutdown {
    get; set;
  }

  [JsonPropertyName("unexpectedShutdownAt")]
  public string? UnexpectedShutdownAt {
    get; set;
  }

  [JsonPropertyName("clockChanged")]
  public bool ClockChanged {
    get; set;
  }

  [JsonPropertyName("clockChangedAt")]
  public string? ClockChangedAt {
    get; set;
  }

  [JsonPropertyName("eventLogRestarted")]
  public bool EventLogRestarted {
    get; set;
  }

  [JsonPropertyName("eventLogStartAt")]
  public string? EventLogStartAt {
    get; set;
  }

  [JsonPropertyName("eventLogStartNote")]
  public string? EventLogStartNote {
    get; set;
  }

  [JsonPropertyName("lastDeviceChange")]
  public string? LastDeviceChange {
    get; set;
  }

  [JsonPropertyName("usnDeleted")]
  public ServiceSignal? UsnDeleted {
    get; set;
  }

  [JsonPropertyName("logClosed")]
  public ServiceSignal? LogClosed {
    get; set;
  }

  [JsonPropertyName("logFull")]
  public ServiceSignal? LogFull {
    get; set;
  }

  [JsonPropertyName("procCreated")]
  public ServiceSignal? ProcCreated {
    get; set;
  }

  [JsonPropertyName("serviceInstalled")]
  public ServiceSignal? ServiceInstalled {
    get; set;
  }

  [JsonPropertyName("usbIn")]
  public ServiceSignal? UsbIn {
    get; set;
  }

  [JsonPropertyName("usbOut")]
  public ServiceSignal? UsbOut {
    get; set;
  }

  [JsonPropertyName("usbDevices")]
  public List<UsbDevice> UsbDevices { get; set; } = new();

  [JsonPropertyName("av")]
  public List<AvProduct> AvProducts { get; set; } = new();
}
public sealed class UsbDevice {
  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("serial")]
  public string Serial { get; set; } = "";
}

public sealed class AvProduct {
  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("state")]
  public string State { get; set; } = "";

  [JsonPropertyName("source")]
  public string Source { get; set; } = "";
}

public sealed class ServiceSignal {
  [JsonPropertyName("key")]
  public string Key { get; set; } = "";

  [JsonPropertyName("detected")]
  public bool Detected {
    get; set;
  }

  [JsonPropertyName("when")]
  public string? When {
    get; set;
  }

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}
public sealed class ServiceUsnInfo {
  [JsonPropertyName("volume")]
  public string Volume { get; set; } = "";

  [JsonPropertyName("state")]
  public string State { get; set; } = "";

  [JsonPropertyName("when")]
  public string? When {
    get; set;
  }

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}
public sealed class RecycleBinInfo {
  [JsonPropertyName("skipped")]
  public bool Skipped {
    get; set;
  }

  [JsonPropertyName("volumes")]
  public int Volumes {
    get; set;
  }

  [JsonPropertyName("items")]
  public int Items {
    get; set;
  }

  [JsonPropertyName("totalSize")]
  public long TotalSize {
    get; set;
  }

  [JsonPropertyName("totalSizeLabel")]
  public string TotalSizeLabel { get; set; } = "0 B";

  [JsonPropertyName("deletedThisSession")]
  public int DeletedThisSession {
    get; set;
  }

  [JsonPropertyName("inaccessible")]
  public int Inaccessible {
    get; set;
  }

  [JsonPropertyName("newestAt")]
  public string? NewestAt {
    get; set;
  }

  [JsonPropertyName("newestPath")]
  public string? NewestPath {
    get; set;
  }

  [JsonPropertyName("oldestAt")]
  public string? OldestAt {
    get; set;
  }

  [JsonPropertyName("oldestPath")]
  public string? OldestPath {
    get; set;
  }
}
