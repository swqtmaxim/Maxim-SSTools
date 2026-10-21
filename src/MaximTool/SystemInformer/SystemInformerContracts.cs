using System.Text.Json.Serialization;

public sealed class SystemInformerStatus {
  [JsonPropertyName("installed")]
  public bool Installed {
    get; set;
  }

  [JsonPropertyName("version")]
  public string? Version {
    get; set;
  }

  [JsonPropertyName("installedTag")]
  public string? InstalledTag {
    get; set;
  }

  [JsonPropertyName("lastDownload")]
  public string? LastDownload {
    get; set;
  }

  [JsonPropertyName("exePath")]
  public string? ExePath {
    get; set;
  }

  [JsonPropertyName("folder")]
  public string? Folder {
    get; set;
  }

  [JsonPropertyName("fileSizeBytes")]
  public long FileSizeBytes {
    get; set;
  }

  [JsonPropertyName("fileSizeLabel")]
  public string? FileSizeLabel {
    get; set;
  }

  [JsonPropertyName("verified")]
  public bool Verified {
    get; set;
  }

  [JsonPropertyName("latestVersion")]
  public string? LatestVersion {
    get; set;
  }

  [JsonPropertyName("latestPublishedAt")]
  public string? LatestPublishedAt {
    get; set;
  }

  [JsonPropertyName("lastCheckedAt")]
  public string? LastCheckedAt {
    get; set;
  }

  [JsonPropertyName("updateAvailable")]
  public bool UpdateAvailable {
    get; set;
  }

  [JsonPropertyName("checkNote")]
  public string? CheckNote {
    get; set;
  }

  [JsonPropertyName("osArch")]
  public string? OsArch {
    get; set;
  }

  [JsonPropertyName("exeArch")]
  public string? ExeArch {
    get; set;
  }

  [JsonPropertyName("archMismatch")]
  public bool ArchMismatch {
    get; set;
  }
}

public sealed class SystemInformerUpdate {
  [JsonPropertyName("ok")]
  public bool Ok {
    get; set;
  }

  [JsonPropertyName("offline")]
  public bool Offline {
    get; set;
  }

  [JsonPropertyName("currentVersion")]
  public string? CurrentVersion {
    get; set;
  }

  [JsonPropertyName("latestVersion")]
  public string? LatestVersion {
    get; set;
  }

  [JsonPropertyName("publishedAt")]
  public string? PublishedAt {
    get; set;
  }

  [JsonPropertyName("updateAvailable")]
  public bool UpdateAvailable {
    get; set;
  }

  [JsonPropertyName("checkedAt")]
  public string? CheckedAt {
    get; set;
  }

  [JsonPropertyName("error")]
  public string? Error {
    get; set;
  }
}

public sealed class SystemInformerProgress {
  [JsonPropertyName("active")]
  public bool Active {
    get; set;
  }

  [JsonPropertyName("done")]
  public bool Done {
    get; set;
  }

  [JsonPropertyName("ok")]
  public bool Ok {
    get; set;
  }

  [JsonPropertyName("stage")]
  public string Stage { get; set; } = "idle";

  [JsonPropertyName("percent")]
  public int Percent {
    get; set;
  }

  [JsonPropertyName("downloadedBytes")]
  public long DownloadedBytes {
    get; set;
  }

  [JsonPropertyName("totalBytes")]
  public long TotalBytes {
    get; set;
  }

  [JsonPropertyName("version")]
  public string? Version {
    get; set;
  }

  [JsonPropertyName("exePath")]
  public string? ExePath {
    get; set;
  }

  [JsonPropertyName("error")]
  public string? Error {
    get; set;
  }
}
