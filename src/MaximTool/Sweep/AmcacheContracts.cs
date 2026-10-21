using System.Text.Json.Serialization;

public sealed class AmcacheReport {
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

  [JsonPropertyName("keysScanned")]
  public int KeysScanned {
    get; set;
  }

  [JsonPropertyName("entries")]
  public List<AmcacheEntry> Entries { get; set; } = new();
}

public sealed class AmcacheEntry {
  [JsonPropertyName("file")]
  public string File { get; set; } = "";

  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("details")]
  public string Details { get; set; } = "";

  [JsonPropertyName("keyTime")]
  public string KeyTime { get; set; } = "";

  [JsonPropertyName("keyUnix")]
  public long KeyUnix {
    get; set;
  }

  [JsonPropertyName("missing")]
  public bool Missing {
    get; set;
  }

  [JsonPropertyName("reasons")]
  public List<string> Reasons { get; set; } = new();

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}
