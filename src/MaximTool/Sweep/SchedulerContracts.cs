using System.Text.Json.Serialization;

public sealed class SchedulerReport {
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

  [JsonPropertyName("taskCount")]
  public int TaskCount {
    get; set;
  }

  [JsonPropertyName("skipped")]
  public int Skipped {
    get; set;
  }

  [JsonPropertyName("entries")]
  public List<SchedulerEntry> Entries { get; set; } = new();
}

public sealed class SchedulerEntry {
  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("folder")]
  public string Folder { get; set; } = "";

  [JsonPropertyName("enabled")]
  public bool Enabled {
    get; set;
  }

  [JsonPropertyName("state")]
  public string State { get; set; } = "";

  [JsonPropertyName("triggers")]
  public List<string> Triggers { get; set; } = new();

  [JsonPropertyName("action")]
  public string Action { get; set; } = "";

  [JsonPropertyName("args")]
  public string Args { get; set; } = "";

  [JsonPropertyName("author")]
  public string Author { get; set; } = "";

  [JsonPropertyName("created")]
  public string Created { get; set; } = "";

  [JsonPropertyName("createdUnix")]
  public long CreatedUnix {
    get; set;
  }

  [JsonPropertyName("lastRun")]
  public string LastRun { get; set; } = "";

  [JsonPropertyName("runLevel")]
  public string RunLevel { get; set; } = "";

  [JsonPropertyName("missing")]
  public bool Missing {
    get; set;
  }

  [JsonPropertyName("reasons")]
  public List<string> Reasons { get; set; } = new();

  [JsonPropertyName("severity")]
  public string Severity { get; set; } = "ok";
}
