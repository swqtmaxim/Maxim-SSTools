using System.Text.Json.Serialization;

public sealed class UsnReport {
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

  [JsonPropertyName("drive")]
  public string Drive { get; set; } = "";

  [JsonPropertyName("scanned")]
  public long Scanned {
    get; set;
  }

  [JsonPropertyName("bootUnix")]
  public long BootUnix {
    get; set;
  }

  [JsonPropertyName("truncated")]
  public bool Truncated {
    get; set;
  }

  [JsonPropertyName("batches")]
  public long Batches {
    get; set;
  }

  [JsonPropertyName("skips")]
  public int Skips {
    get; set;
  }

  [JsonPropertyName("dirsMapped")]
  public int DirsMapped {
    get; set;
  }

  [JsonPropertyName("built")]
  public string Built { get; set; } = "";

  [JsonPropertyName("resolved")]
  public int Resolved {
    get; set;
  }

  [JsonPropertyName("dated")]
  public int Dated {
    get; set;
  }

  [JsonPropertyName("returned")]
  public int Returned {
    get; set;
  }

  [JsonPropertyName("matched")]
  public int Matched {
    get; set;
  }

  [JsonPropertyName("entries")]
  public List<UsnEntry> Entries { get; set; } = new();
}

public sealed class UsnEntry {
  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("ext")]
  public string Ext { get; set; } = "";

  [JsonPropertyName("reason")]
  public string Reason { get; set; } = "";

  [JsonPropertyName("time")]
  public string Time { get; set; } = "";

  [JsonPropertyName("unix")]
  public long Unix {
    get; set;
  }

  [JsonPropertyName("usn")]
  public long Usn {
    get; set;
  }

  [JsonPropertyName("gone")]
  public bool Gone {
    get; set;
  }
}
