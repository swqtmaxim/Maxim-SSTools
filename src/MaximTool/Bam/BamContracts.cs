using System.Text.Json.Serialization;

public sealed class BamReport {
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

  [JsonPropertyName("drivesScanned")]
  public int DrivesScanned {
    get; set;
  }

  [JsonPropertyName("drivesSkipped")]
  public int DrivesSkipped {
    get; set;
  }

  [JsonPropertyName("entries")]
  public List<BamEntry> Entries { get; set; } = new();
}

public sealed class BamEntry {
  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("readableTime")]
  public string ReadableTime { get; set; } = "";

  [JsonPropertyName("executedUnix")]
  public long ExecutedUnix {
    get; set;
  }

  [JsonPropertyName("signature")]
  public string Signature { get; set; } = "";

  [JsonPropertyName("isInInstance")]
  public bool IsInInstance {
    get; set;
  }

  [JsonPropertyName("matchedRules")]
  public List<string> MatchedRules { get; set; } = new();

  [JsonPropertyName("matchedDetails")]
  public List<PrefetchRuleHit> MatchedDetails { get; set; } = new();

  [JsonPropertyName("replaceResults")]
  public List<BamReplaceResult> ReplaceResults { get; set; } = new();
}

public sealed class BamReplaceResult {
  [JsonPropertyName("filename")]
  public string Filename { get; set; } = "";

  [JsonPropertyName("replaceType")]
  public string ReplaceType { get; set; } = "";

  [JsonPropertyName("details")]
  public string Details { get; set; } = "";
}
