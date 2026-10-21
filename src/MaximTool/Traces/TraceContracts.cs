using System.Text.Json.Serialization;

public sealed class PrefetchReport {
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

  [JsonPropertyName("prefetchDir")]
  public string PrefetchDir { get; set; } = "";

  [JsonPropertyName("filesFound")]
  public int FilesFound {
    get; set;
  }

  [JsonPropertyName("entries")]
  public List<PrefetchEntry> Entries { get; set; } = new();
}

public sealed class PrefetchEntry {
  [JsonPropertyName("filename")]
  public string Filename { get; set; } = "";

  [JsonPropertyName("exeName")]
  public string ExeName { get; set; } = "";

  [JsonPropertyName("runCount")]
  public int RunCount {
    get; set;
  }

  [JsonPropertyName("prefetchHash")]
  public string PrefetchHash { get; set; } = "";

  [JsonPropertyName("properPath")]
  public string ProperPath { get; set; } = "";

  [JsonPropertyName("readableTime")]
  public string ReadableTime { get; set; } = "";

  [JsonPropertyName("executedUnix")]
  public long ExecutedUnix {
    get; set;
  }

  [JsonPropertyName("lastRunsUnix")]
  public List<long> LastRunsUnix { get; set; } = new();

  [JsonPropertyName("lastRunsExact")]
  public List<string> LastRunsExact { get; set; } = new();

  [JsonPropertyName("relatedFiles")]
  public List<PrefetchRelatedFile> RelatedFiles { get; set; } = new();

  [JsonPropertyName("relatedTotal")]
  public int RelatedTotal {
    get; set;
  }

  [JsonPropertyName("isSigned")]
  public bool IsSigned {
    get; set;
  }

  [JsonPropertyName("isPresent")]
  public bool IsPresent {
    get; set;
  }

  [JsonPropertyName("isInInstance")]
  public bool IsInInstance {
    get; set;
  }

  [JsonPropertyName("matchedRules")]
  public List<string> MatchedRules { get; set; } = new();

  [JsonPropertyName("matchedDetails")]
  public List<PrefetchRuleHit> MatchedDetails { get; set; } = new();

  [JsonPropertyName("pfSizeBytes")]
  public long PfSizeBytes {
    get; set;
  }

  [JsonPropertyName("pfCreated")]
  public string PfCreated { get; set; } = "";

  [JsonPropertyName("pfAccessed")]
  public string PfAccessed { get; set; } = "";

  [JsonPropertyName("pfModified")]
  public string PfModified { get; set; } = "";
}

public sealed class PrefetchRelatedFile {
  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("present")]
  public bool Present {
    get; set;
  }
}

public sealed class PrefetchRuleHit {
  [JsonPropertyName("id")]
  public string Id { get; set; } = "";

  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("sample")]
  public string Sample { get; set; } = "";

  [JsonPropertyName("encoding")]
  public string Encoding { get; set; } = "";

  [JsonPropertyName("offset")]
  public long Offset {
    get; set;
  }

  [JsonPropertyName("count")]
  public int Count {
    get; set;
  }

  [JsonPropertyName("context")]
  public string Context { get; set; } = "";
}

public sealed class PrefetchRelatedSig {
  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("signed")]
  public bool Signed {
    get; set;
  }
}
