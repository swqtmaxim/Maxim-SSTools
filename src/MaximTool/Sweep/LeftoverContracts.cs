using System.Text.Json.Serialization;

public sealed class LeftoverReport {
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

  [JsonPropertyName("tempFiles")]
  public List<LeftoverFile> TempFiles { get; set; } = new();

  [JsonPropertyName("recentLinks")]
  public List<LeftoverFile> RecentLinks { get; set; } = new();

  [JsonPropertyName("dumps")]
  public List<LeftoverFile> Dumps { get; set; } = new();

  [JsonPropertyName("hosts")]
  public List<HostEntry> Hosts { get; set; } = new();

  [JsonPropertyName("hostsModified")]
  public bool HostsModified {
    get; set;
  }

  [JsonPropertyName("history")]
  public List<HistoryHit> History { get; set; } = new();
}

public sealed class LeftoverFile {
  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("sizeLabel")]
  public string SizeLabel { get; set; } = "";

  [JsonPropertyName("modified")]
  public string Modified { get; set; } = "";

  [JsonPropertyName("kind")]
  public string Kind { get; set; } = "";

  [JsonPropertyName("note")]
  public string Note { get; set; } = "";

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}

public sealed class HostEntry {
  [JsonPropertyName("ip")]
  public string Ip { get; set; } = "";

  [JsonPropertyName("host")]
  public string Host { get; set; } = "";

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}

public sealed class HistoryHit {
  [JsonPropertyName("file")]
  public string File { get; set; } = "";

  [JsonPropertyName("preview")]
  public string Preview { get; set; } = "";

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}
