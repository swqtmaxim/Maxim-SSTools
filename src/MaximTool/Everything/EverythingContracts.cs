using System.Text.Json.Serialization;

public sealed class EverythingStatus {
  [JsonPropertyName("installed")]
  public bool Installed {
    get; set;
  }

  [JsonPropertyName("version")]
  public string? Version {
    get; set;
  }

  [JsonPropertyName("latestKnown")]
  public string? LatestKnown {
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

  [JsonPropertyName("fileSizeLabel")]
  public string? FileSizeLabel {
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
}

public sealed class EverythingProgress {
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
